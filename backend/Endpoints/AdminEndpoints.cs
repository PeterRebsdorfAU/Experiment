using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AiExperiment.Api.Data;
using AiExperiment.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AiExperiment.Api.Endpoints;

/// <summary>Admin-API. Kræver headeren X-Admin-Key = adgangskoden fra konfigurationen (Admin:Password).</summary>
public static partial class AdminEndpoints
{
    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z.]{0,63}$")]
    private static partial Regex TextKeyRegex();

    public record SettingsDto(bool StudyOpen, int MinCorrectForLottery);
    public record QuestionDto(int Id, int SortOrder, string Title, string Text, decimal? CorrectAnswer,
        decimal Tolerance, string AiResponse, decimal? AiAnswerValue);
    public record QuestionInput(string? Title, string? Text, decimal? CorrectAnswer, decimal? Tolerance,
        string? AiResponse, decimal? AiAnswerValue);
    public record ResetRequest(string? Confirm, bool IncludeLottery);

    public static void MapAdminEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").AddEndpointFilter(async (ctx, next) =>
        {
            var password = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Admin:Password"];
            if (string.IsNullOrWhiteSpace(password))
                return Results.Problem("Admin-adgangskode er ikke sat på serveren (Admin__Password).", statusCode: 503);
            var given = ctx.HttpContext.Request.Headers["X-Admin-Key"].ToString();
            var ok = CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(given)), SHA256.HashData(Encoding.UTF8.GetBytes(password)));
            return ok ? await next(ctx) : Results.Unauthorized();
        });

        admin.MapGet("/overview", async (AppDb db) =>
        {
            var s = await db.Settings.AsNoTracking().FirstAsync();
            var ps = await db.Participants.AsNoTracking()
                .OrderByDescending(p => p.ConsentAt)
                .Select(p => new
                {
                    p.Code, p.IsTest, p.Group, p.Step, p.TaskIndex, p.ConsentAt, p.FinishedAt,
                    Total = p.QuestionIds.Length == 0 ? 0 : p.QuestionIds.Length - p.QuestionIds.Replace(",", "").Length + 1,
                    AiClicks = p.Tasks.Sum(t => t.AiClicks),
                    Correct = p.Tasks.Count(t => t.IsCorrect == true),
                })
                .ToListAsync();
            var real = ps.Where(p => !p.IsTest).ToList();

            object Count(string? group) => new
            {
                assigned = real.Count(p => p.Group == group),
                completed = real.Count(p => p.Group == group && p.FinishedAt != null),
                inProgress = real.Count(p => p.Group == group && p.FinishedAt == null),
            };

            return Results.Ok(new
            {
                settings = new SettingsDto(s.StudyOpen, s.MinCorrectForLottery),
                counts = new
                {
                    high = Count(Groups.High),
                    low = Count(Groups.Low),
                    notAssigned = real.Count(p => p.Group == null),
                    lotteryEntries = await db.LotteryEntries.CountAsync(),
                    events = await db.Events.CountAsync(),
                },
                participants = ps.Take(300),
            });
        });

        admin.MapPut("/settings", async (SettingsDto dto, AppDb db) =>
        {
            if (dto.MinCorrectForLottery < 0) return Results.BadRequest("Ugyldigt antal.");
            var s = await db.Settings.FirstAsync();
            s.StudyOpen = dto.StudyOpen;
            s.MinCorrectForLottery = dto.MinCorrectForLottery;
            await db.SaveChangesAsync();
            return Results.Ok(new SettingsDto(s.StudyOpen, s.MinCorrectForLottery));
        });

        admin.MapGet("/questions", async (AppDb db) =>
            Results.Ok(await db.Questions.AsNoTracking().Where(q => !q.Archived)
                .OrderBy(q => q.SortOrder).ThenBy(q => q.Id).Select(q => ToDto(q)).ToListAsync()));

        admin.MapPost("/questions", async (QuestionInput input, AppDb db) =>
        {
            var q = new Question();
            if (Apply(q, input) is { } error) return Results.BadRequest(error);
            q.SortOrder = (await db.Questions.Where(x => !x.Archived).MaxAsync(x => (int?)x.SortOrder) ?? 0) + 1;
            db.Questions.Add(q);
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(q));
        });

        admin.MapPut("/questions/{id:int}", async (int id, QuestionInput input, AppDb db) =>
        {
            var q = await db.Questions.FirstOrDefaultAsync(x => x.Id == id && !x.Archived);
            if (q is null) return Results.NotFound();
            if (Apply(q, input) is { } error) return Results.BadRequest(error);
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(q));
        });

        // "Slet" arkiverer: nye deltagere får ikke spørgsmålet, men allerede indsamlede svar bevares.
        admin.MapDelete("/questions/{id:int}", async (int id, AppDb db) =>
        {
            var q = await db.Questions.FirstOrDefaultAsync(x => x.Id == id);
            if (q is null) return Results.NotFound();
            if (await db.TaskResponses.AnyAsync(t => t.QuestionId == id)) q.Archived = true;
            else db.Questions.Remove(q);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        admin.MapPut("/questions/order", async (int[] ids, AppDb db) =>
        {
            var qs = await db.Questions.Where(q => ids.Contains(q.Id)).ToListAsync();
            for (var i = 0; i < ids.Length; i++)
                if (qs.FirstOrDefault(q => q.Id == ids[i]) is { } q) q.SortOrder = i + 1;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Gemmer ændrede tekster. En tom værdi (eller null) betyder "brug standardteksten igen".
        admin.MapPut("/texts", async (Dictionary<string, string?> texts, AppDb db) =>
        {
            if (texts.Count > 60) return Results.BadRequest("For mange tekster.");
            if (texts.Keys.Any(k => !TextKeyRegex().IsMatch(k))) return Results.BadRequest("Ugyldig tekst-nøgle.");
            if (texts.Values.Any(v => v is { Length: > 20000 })) return Results.BadRequest("Teksten er for lang.");

            var existing = await db.TextSettings.Where(t => texts.Keys.Contains(t.Key)).ToDictionaryAsync(t => t.Key);
            foreach (var (key, value) in texts)
            {
                existing.TryGetValue(key, out var row);
                if (string.IsNullOrWhiteSpace(value))
                {
                    if (row is not null) db.TextSettings.Remove(row);
                }
                else if (row is null) db.TextSettings.Add(new TextSetting { Key = key, Value = value });
                else row.Value = value;
            }
            await db.SaveChangesAsync();
            return Results.Ok(await db.TextSettings.AsNoTracking().ToDictionaryAsync(t => t.Key, t => t.Value));
        });

        // Sletter én deltager med svar og hændelseslog. Lodtræknings-e-mails kan ikke kobles til deltageren
        // og påvirkes derfor ikke. Fordelingen tager højde for det: næste deltager går i den mindste gruppe.
        admin.MapDelete("/participants/{code}", async (string code, AppDb db) =>
        {
            var p = await db.Participants.FirstOrDefaultAsync(x => x.Code == code);
            if (p is null) return Results.NotFound();
            await db.Events.Where(e => e.ParticipantId == p.Id).ExecuteDeleteAsync();
            await db.TaskResponses.Where(t => t.ParticipantId == p.Id).ExecuteDeleteAsync();
            db.Participants.Remove(p);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        admin.MapGet("/export/{name}", async (string name, AppDb db) =>
        {
            var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
            return name switch
            {
                "participants.csv" => Csv(await Exporter.ParticipantsAsync(db), $"deltagere_{stamp}.csv"),
                "tasks.csv" => Csv(await Exporter.TasksAsync(db), $"opgaver_{stamp}.csv"),
                "events.csv" => Csv(await Exporter.EventsAsync(db), $"haendelser_{stamp}.csv"),
                "lottery.csv" => Csv(await Exporter.LotteryAsync(db), $"lodtraekning_{stamp}.csv"),
                // Excel-filen indeholder bevidst IKKE e-mails, så den kan deles uden persondata.
                "all.xlsx" => Results.File(Exporter.ToXlsx(
                    [await Exporter.ParticipantsAsync(db), await Exporter.TasksAsync(db), await Exporter.EventsAsync(db)]),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"eksperiment_{stamp}.xlsx"),
                _ => Results.NotFound(),
            };
        });

        // Sletter alle deltagerdata (fx efter pilottest). Spørgsmål og indstillinger bevares.
        admin.MapPost("/reset", async (ResetRequest req, AppDb db) =>
        {
            if (req.Confirm != "SLET") return Results.BadRequest("Skriv SLET for at bekræfte.");
            await db.Events.ExecuteDeleteAsync();
            await db.TaskResponses.ExecuteDeleteAsync();
            await db.Participants.ExecuteDeleteAsync();
            if (req.IncludeLottery) await db.LotteryEntries.ExecuteDeleteAsync();
            return Results.NoContent();
        });
    }

    private static IResult Csv(DataTable t, string fileName) =>
        Results.File(Exporter.ToCsv(t), "text/csv; charset=utf-8", fileName);

    private static QuestionDto ToDto(Question q) =>
        new(q.Id, q.SortOrder, q.Title, q.Text, q.CorrectAnswer, q.Tolerance, q.AiResponse, q.AiAnswerValue);

    private static string? Apply(Question q, QuestionInput i)
    {
        if (string.IsNullOrWhiteSpace(i.Text)) return "Opgaveteksten mangler.";
        if (string.IsNullOrWhiteSpace(i.AiResponse)) return "AI-svaret mangler.";
        if (i.Tolerance is < 0) return "Tolerancen kan ikke være negativ.";
        q.Title = (i.Title ?? "").Trim();
        q.Text = i.Text.Trim();
        q.CorrectAnswer = i.CorrectAnswer;
        q.Tolerance = i.Tolerance ?? 0.01m;
        q.AiResponse = i.AiResponse.Trim();
        q.AiAnswerValue = i.AiAnswerValue;
        return null;
    }
}
