using System.Text.RegularExpressions;
using AiExperiment.Api.Data;
using AiExperiment.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AiExperiment.Api.Endpoints;

/// <summary>
/// Deltagerens API. Serveren styrer hvilket trin deltageren er på, så man ikke kan gå tilbage
/// eller springe over – heller ikke ved at genindlæse siden eller trykke "tilbage" i browseren.
/// </summary>
public static partial class ParticipantEndpoints
{
    public record TaskDto(int QuestionId, int Position, int Total, string Title, string Text, bool AiShown, string? AiResponse);
    public record StateDto(Guid ParticipantId, string Code, string Step, string? Group, bool IsTest,
        TaskDto? Task, DateTime? TasksStartedAt, bool LotterySubmitted, DateTime ServerNow);
    public record StartRequest(string? TestGroup);
    public record AnswerRequest(int QuestionId, string? Answer);
    public record AiHelpRequest(int QuestionId);
    public record ManipulationRequest(int Value);
    public record BackgroundRequest(string? Gender, string? AiUsageFrequency);
    public record LotteryRequest(string? Email);
    public record ClientEvent(string? Id, string Type, int? QuestionId, DateTime? ClientTime, string? Data);
    public record EventBatch(List<ClientEvent> Events);

    public static readonly string[] GenderOptions = ["Kvinde", "Mand", "Andet", "Ønsker ikke at svare"];
    public static readonly string[] AiUsageOptions = ["Aldrig", "Sjældnere end månedligt", "Månedligt", "Ugentligt", "Dagligt"];

    public static void MapParticipantEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/status", async (AppDb db) =>
        {
            var s = await db.Settings.AsNoTracking().FirstAsync();
            return Results.Ok(new { open = s.StudyOpen });
        });

        // Tekster rettet i admin (kun ændrede; resten er standardteksterne i frontend).
        api.MapGet("/texts", async (AppDb db) =>
            Results.Ok(await db.TextSettings.AsNoTracking().ToDictionaryAsync(t => t.Key, t => t.Value)));

        // Oprettes når deltageren trykker "Jeg accepterer" på samtykkesiden.
        api.MapPost("/participants", async (StartRequest req, AppDb db) =>
        {
            var isTest = req.TestGroup is Groups.High or Groups.Low;
            var settings = await db.Settings.AsNoTracking().FirstAsync();
            if (!settings.StudyOpen && !isTest)
                return Results.Problem("Eksperimentet er ikke åbent.", statusCode: 423);

            var p = new Participant
            {
                Id = Guid.NewGuid(),
                Code = await NewCodeAsync(db, isTest),
                IsTest = isTest,
                Group = isTest ? req.TestGroup : null,
                Step = Steps.Instructions,
                ConsentAt = DateTime.UtcNow,
            };
            db.Participants.Add(p);
            Log(db, p, "consent_accepted");
            await db.SaveChangesAsync();
            return Results.Ok(await BuildStateAsync(db, p));
        });

        var one = api.MapGroup("/participants/{id:guid}");

        one.MapGet("/", async (Guid id, AppDb db) =>
        {
            var p = await db.Participants.FindAsync(id);
            return p is null ? Results.NotFound() : Results.Ok(await BuildStateAsync(db, p));
        });

        // Instruktion læst -> tilfældig fordeling i gruppe.
        one.MapPost("/instructions-done", async (Guid id, AppDb db, Randomizer randomizer, CancellationToken ct) =>
        {
            var p = await db.Participants.FindAsync([id], ct);
            if (p is null) return Results.NotFound();
            if (p.Step != Steps.Instructions) return Conflict(await BuildStateAsync(db, p));

            var questionIds = await db.Questions.Where(q => !q.Archived).OrderBy(q => q.SortOrder).ThenBy(q => q.Id)
                .Select(q => q.Id).ToListAsync(ct);
            if (questionIds.Count == 0) return Results.Problem("Der er ingen opgaver oprettet.", statusCode: 503);

            p.QuestionIds = string.Join(',', questionIds);
            p.Step = Steps.Notice;
            await randomizer.AssignAsync(db, p, ct); // gemmer også
            Log(db, p, "group_assigned", data: p.Group);
            await db.SaveChangesAsync(ct);
            return Results.Ok(await BuildStateAsync(db, p));
        });

        // Gruppebesked læst -> første opgave.
        one.MapPost("/notice-done", async (Guid id, AppDb db) =>
        {
            var p = await db.Participants.FindAsync(id);
            if (p is null) return Results.NotFound();
            if (p.Step != Steps.Notice) return Conflict(await BuildStateAsync(db, p));

            var now = DateTime.UtcNow;
            p.Step = Steps.Task;
            p.TaskIndex = 0;
            p.TasksStartedAt = now;
            StartTask(db, p, now);
            await db.SaveChangesAsync();
            return Results.Ok(await BuildStateAsync(db, p));
        });

        one.MapPost("/ai-help", async (Guid id, AiHelpRequest req, AppDb db) =>
        {
            var p = await db.Participants.FindAsync(id);
            if (p is null) return Results.NotFound();
            var task = await CurrentTaskAsync(db, p);
            if (task is null || task.QuestionId != req.QuestionId) return Conflict(await BuildStateAsync(db, p));

            var now = DateTime.UtcNow;
            task.AiClicks++;
            task.FirstAiClickAt ??= now;
            Log(db, p, "ai_help_click", task.QuestionId, $"click={task.AiClicks}");
            await db.SaveChangesAsync();

            var q = await db.Questions.AsNoTracking().FirstAsync(x => x.Id == task.QuestionId);
            return Results.Ok(new { aiResponse = q.AiResponse, clicks = task.AiClicks });
        });

        one.MapPost("/answer", async (Guid id, AnswerRequest req, AppDb db) =>
        {
            var p = await db.Participants.FindAsync(id);
            if (p is null) return Results.NotFound();
            var task = await CurrentTaskAsync(db, p);
            if (task is null || task.QuestionId != req.QuestionId) return Conflict(await BuildStateAsync(db, p));

            var raw = (req.Answer ?? "").Trim();
            if (raw.Length > 100) raw = raw[..100];
            var q = await db.Questions.AsNoTracking().FirstAsync(x => x.Id == task.QuestionId);
            var now = DateTime.UtcNow;

            task.AnswerRaw = raw;
            task.AnswerValue = AnswerParser.Parse(raw);
            task.IsCorrect = AnswerParser.Matches(task.AnswerValue, q.CorrectAnswer, q.Tolerance);
            task.MatchesAi = q.AiAnswerValue.HasValue
                ? AnswerParser.Matches(task.AnswerValue, q.AiAnswerValue, q.Tolerance)
                : null;
            task.SubmittedAt = now;
            task.TimeSpentMs = (long)(now - task.ShownAt).TotalMilliseconds;
            Log(db, p, "answer_submitted", task.QuestionId, raw);

            p.TaskIndex++;
            if (p.TaskIndex < p.GetQuestionIds().Length)
            {
                StartTask(db, p, now);
            }
            else
            {
                p.Step = Steps.Manipulation;
                p.TasksEndedAt = now;
            }
            await db.SaveChangesAsync();
            return Results.Ok(await BuildStateAsync(db, p));
        });

        one.MapPost("/manipulation", async (Guid id, ManipulationRequest req, AppDb db) =>
        {
            var p = await db.Participants.FindAsync(id);
            if (p is null) return Results.NotFound();
            if (p.Step != Steps.Manipulation) return Conflict(await BuildStateAsync(db, p));
            if (req.Value is < 1 or > 7) return Results.BadRequest("Værdien skal være 1-7.");

            p.ManipulationCheck = req.Value;
            p.Step = Steps.Background;
            Log(db, p, "manipulation_check", data: req.Value.ToString());
            await db.SaveChangesAsync();
            return Results.Ok(await BuildStateAsync(db, p));
        });

        one.MapPost("/background", async (Guid id, BackgroundRequest req, AppDb db) =>
        {
            var p = await db.Participants.FindAsync(id);
            if (p is null) return Results.NotFound();
            if (p.Step != Steps.Background) return Conflict(await BuildStateAsync(db, p));
            if (!GenderOptions.Contains(req.Gender) || !AiUsageOptions.Contains(req.AiUsageFrequency))
                return Results.BadRequest("Ugyldigt svar.");

            p.Gender = req.Gender;
            p.AiUsageFrequency = req.AiUsageFrequency;
            p.Step = Steps.Debrief;
            p.FinishedAt = DateTime.UtcNow;
            Log(db, p, "background_submitted");
            Log(db, p, "finished");
            await db.SaveChangesAsync();
            return Results.Ok(await BuildStateAsync(db, p));
        });

        // E-mail gemmes i en separat tabel UDEN deltager-ID. Vi gemmer kun om personen opfyldte
        // kravet til lodtrækningen – ikke hvilke svar der hører til.
        one.MapPost("/lottery", async (Guid id, LotteryRequest req, AppDb db) =>
        {
            var p = await db.Participants.Include(x => x.Tasks).FirstOrDefaultAsync(x => x.Id == id);
            if (p is null) return Results.NotFound();
            if (p.Step != Steps.Debrief || p.LotterySubmitted) return Conflict(await BuildStateAsync(db, p));
            var email = (req.Email ?? "").Trim();
            if (email.Length is < 5 or > 200 || !EmailRegex().IsMatch(email))
                return Results.BadRequest("Ugyldig e-mail.");

            var settings = await db.Settings.AsNoTracking().FirstAsync();
            var correct = p.Tasks.Count(t => t.IsCorrect == true);
            if (!p.IsTest)
            {
                db.LotteryEntries.Add(new LotteryEntry
                {
                    Id = Guid.NewGuid(),
                    Email = email,
                    Eligible = correct >= settings.MinCorrectForLottery,
                    SubmittedOn = DateOnly.FromDateTime(DateTime.UtcNow),
                });
            }
            p.LotterySubmitted = true;
            await db.SaveChangesAsync();
            return Results.Ok(await BuildStateAsync(db, p));
        });

        // Rå hændelseslog fra browseren (tastetryk i svarfeltet, kopiering, fanebytte osv.).
        one.MapPost("/events", async (Guid id, EventBatch batch, AppDb db) =>
        {
            if (batch.Events is null || batch.Events.Count == 0) return Results.Ok();
            if (batch.Events.Count > 100) return Results.BadRequest("For mange hændelser.");
            if (!await db.Participants.AnyAsync(x => x.Id == id)) return Results.NotFound();

            var ids = batch.Events.Select(e => e.Id).Where(x => x != null).ToList();
            var existing = ids.Count == 0
                ? []
                : (await db.Events.Where(e => e.ParticipantId == id && ids.Contains(e.ClientEventId))
                    .Select(e => e.ClientEventId).ToListAsync()).ToHashSet();
            var now = DateTime.UtcNow;

            foreach (var e in batch.Events)
            {
                if (!EventTypeRegex().IsMatch(e.Type ?? "")) continue;
                if (e.Id != null && (e.Id.Length > 40 || !existing.Add(e.Id))) continue;
                db.Events.Add(new EventLog
                {
                    ParticipantId = id,
                    ClientEventId = e.Id,
                    QuestionId = e.QuestionId,
                    Type = e.Type!,
                    ServerTime = now,
                    ClientTime = e.ClientTime?.ToUniversalTime(),
                    Data = e.Data is { Length: > 2000 } ? e.Data[..2000] : e.Data,
                });
            }
            await db.SaveChangesAsync();
            return Results.Ok();
        });
    }

    private static void StartTask(AppDb db, Participant p, DateTime now)
    {
        var qid = p.GetQuestionIds()[p.TaskIndex];
        db.TaskResponses.Add(new TaskResponse
        {
            ParticipantId = p.Id, QuestionId = qid, Position = p.TaskIndex + 1, ShownAt = now,
        });
        Log(db, p, "task_shown", qid, $"position={p.TaskIndex + 1}");
    }

    private static Task<TaskResponse?> CurrentTaskAsync(AppDb db, Participant p) =>
        p.Step != Steps.Task
            ? Task.FromResult<TaskResponse?>(null)
            : db.TaskResponses.FirstOrDefaultAsync(t => t.ParticipantId == p.Id && t.Position == p.TaskIndex + 1);

    private static void Log(AppDb db, Participant p, string type, int? questionId = null, string? data = null) =>
        db.Events.Add(new EventLog
        {
            ParticipantId = p.Id, QuestionId = questionId, Type = type, ServerTime = DateTime.UtcNow, Data = data,
        });

    private static IResult Conflict(StateDto state) => Results.Conflict(state);

    private static async Task<StateDto> BuildStateAsync(AppDb db, Participant p)
    {
        TaskDto? taskDto = null;
        if (p.Step == Steps.Task)
        {
            var ids = p.GetQuestionIds();
            var task = await CurrentTaskAsync(db, p);
            var q = await db.Questions.AsNoTracking().FirstAsync(x => x.Id == ids[p.TaskIndex]);
            var aiShown = task?.AiClicks > 0;
            taskDto = new TaskDto(q.Id, p.TaskIndex + 1, ids.Length, q.Title, q.Text, aiShown, aiShown ? q.AiResponse : null);
        }
        return new StateDto(p.Id, p.Code, p.Step, p.Group, p.IsTest, taskDto, p.TasksStartedAt, p.LotterySubmitted, DateTime.UtcNow);
    }

    private static async Task<string> NewCodeAsync(AppDb db, bool isTest)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // uden O/0 og I/1
        while (true)
        {
            var code = (isTest ? "T-" : "P-") + new string(Enumerable.Range(0, 6)
                .Select(_ => alphabet[Random.Shared.Next(alphabet.Length)]).ToArray());
            if (!await db.Participants.AnyAsync(x => x.Code == code)) return code;
        }
    }

    [GeneratedRegex(@"^[a-z_]{1,40}$")]
    private static partial Regex EventTypeRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();
}
