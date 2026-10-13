using System.Globalization;
using System.Text;
using AiExperiment.Api.Data;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace AiExperiment.Api.Services;

public record DataTable(string Name, string[] Headers, List<object?[]> Rows);

/// <summary>
/// Bygger eksport-tabellerne. Tidspunkter er dansk tid (Europe/Copenhagen).
/// Lodtræknings-e-mails er en separat tabel og kan ikke kobles til deltagerne.
/// </summary>
public static class Exporter
{
    private static readonly TimeZoneInfo Dk = FindDk();

    public static async Task<DataTable> ParticipantsAsync(AppDb db)
    {
        var ps = await db.Participants.AsNoTracking().Include(p => p.Tasks).OrderBy(p => p.ConsentAt).ToListAsync();
        var rows = ps.Select(p =>
        {
            var answered = p.Tasks.Where(t => t.SubmittedAt != null).ToList();
            return new object?[]
            {
                p.Code, p.IsTest, p.Group, p.AssignmentIndex, p.Step, p.FinishedAt != null,
                Local(p.ConsentAt), Local(p.AssignedAt), Local(p.TasksStartedAt), Local(p.TasksEndedAt), Local(p.FinishedAt),
                p.FinishedAt is { } f ? Math.Round((f - p.ConsentAt).TotalSeconds, 1) : null,
                p.ManipulationCheck, p.Gender, p.AiUsageFrequency,
                answered.Count, answered.Count(t => t.IsCorrect == true),
                p.Tasks.Sum(t => t.AiClicks), p.Tasks.Count(t => t.AiClicks > 0), p.Tasks.Any(t => t.AiClicks > 0),
                answered.Count(t => t.MatchesAi == true),
            };
        }).ToList();

        return new DataTable("Deltagere",
        [
            "participant_code", "is_test", "group", "assignment_index", "current_step", "completed",
            "consent_at", "assigned_at", "tasks_started_at", "tasks_ended_at", "finished_at", "total_duration_s",
            "manipulation_check", "gender", "ai_usage_frequency",
            "tasks_answered", "correct_count", "total_ai_clicks", "tasks_with_ai_help", "any_ai_used", "answers_matching_ai",
        ], rows);
    }

    public static async Task<DataTable> TasksAsync(AppDb db)
    {
        var questions = await db.Questions.AsNoTracking().ToDictionaryAsync(q => q.Id);
        var tasks = await db.TaskResponses.AsNoTracking().Include(t => t.Participant)
            .OrderBy(t => t.Participant!.ConsentAt).ThenBy(t => t.Position).ToListAsync();

        var rows = tasks.Select(t =>
        {
            questions.TryGetValue(t.QuestionId, out var q);
            return new object?[]
            {
                t.Participant!.Code, t.Participant.IsTest, t.Participant.Group, t.Position, t.QuestionId, q?.Title,
                Local(t.ShownAt), Local(t.SubmittedAt), t.TimeSpentMs is { } ms ? Math.Round(ms / 1000.0, 2) : null,
                t.AiClicks, t.AiClicks > 0, Local(t.FirstAiClickAt),
                t.FirstAiClickAt is { } fc ? Math.Round((fc - t.ShownAt).TotalSeconds, 2) : null,
                t.AnswerRaw, t.AnswerValue, t.IsCorrect, t.MatchesAi, q?.CorrectAnswer, q?.AiAnswerValue,
            };
        }).ToList();

        return new DataTable("Opgaver",
        [
            "participant_code", "is_test", "group", "position", "question_id", "question_title",
            "shown_at", "submitted_at", "time_spent_s",
            "ai_clicks", "ai_used", "first_ai_click_at", "seconds_to_first_ai_click",
            "answer_raw", "answer_value", "is_correct", "matches_ai_answer", "correct_answer", "ai_answer_value",
        ], rows);
    }

    public static async Task<DataTable> EventsAsync(AppDb db)
    {
        var codes = await db.Participants.AsNoTracking()
            .Select(p => new { p.Id, p.Code, p.IsTest, p.Group }).ToDictionaryAsync(p => p.Id);
        var events = await db.Events.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
        var rows = events.Select(e =>
        {
            codes.TryGetValue(e.ParticipantId, out var p);
            return new object?[]
            {
                e.Id, p?.Code, p?.IsTest, p?.Group, e.QuestionId, e.Type, Local(e.ServerTime), Local(e.ClientTime), e.Data,
            };
        }).ToList();

        return new DataTable("Hændelser",
            ["event_id", "participant_code", "is_test", "group", "question_id", "type", "server_time", "client_time", "data"],
            rows);
    }

    public static async Task<DataTable> LotteryAsync(AppDb db)
    {
        var entries = await db.LotteryEntries.AsNoTracking().ToListAsync();
        // Sorteres efter e-mail (ikke indsendelsesrækkefølge), så rækkefølgen ikke afslører noget.
        var rows = entries.OrderBy(e => e.Email, StringComparer.OrdinalIgnoreCase)
            .Select(e => new object?[] { e.Email, e.Eligible, e.SubmittedOn.ToString("yyyy-MM-dd") }).ToList();
        return new DataTable("Lodtrækning", ["email", "eligible", "submitted_on"], rows);
    }

    /// <summary>CSV med komma som skilletegn og punktum som decimaltegn (læses direkte af R, SPSS, Stata, Python).</summary>
    public static byte[] ToCsv(DataTable t)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', t.Headers.Select(Escape)));
        foreach (var row in t.Rows)
            sb.AppendLine(string.Join(',', row.Select(v => Escape(Format(v)))));
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    public static byte[] ToXlsx(IEnumerable<DataTable> tables)
    {
        using var wb = new XLWorkbook();
        foreach (var t in tables)
        {
            var ws = wb.Worksheets.Add(t.Name);
            for (var c = 0; c < t.Headers.Length; c++) ws.Cell(1, c + 1).Value = t.Headers[c];
            for (var r = 0; r < t.Rows.Count; r++)
            for (var c = 0; c < t.Headers.Length; c++)
            {
                var cell = ws.Cell(r + 2, c + 1);
                switch (t.Rows[r][c])
                {
                    case null: break;
                    case DateTime d: cell.Value = d; cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss"; break;
                    case bool b: cell.Value = b ? 1 : 0; break;
                    case int i: cell.Value = i; break;
                    case long l: cell.Value = l; break;
                    case double dbl: cell.Value = dbl; break;
                    case decimal m: cell.Value = m; break;
                    case var v: cell.Value = v.ToString(); break;
                }
            }
            ws.Row(1).Style.Font.Bold = true;
            ws.SheetView.FreezeRows(1);
            if (t.Rows.Count > 0) ws.Range(1, 1, t.Rows.Count + 1, t.Headers.Length).SetAutoFilter();
            // Kolonnebredde kræver skrifttype-målinger; mangler de på serveren, springes det bare over.
            try { ws.Columns().AdjustToContents(1, Math.Min(t.Rows.Count + 1, 200), 8, 60); }
            catch { ws.Columns().Width = 16; }
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static DateTime? Local(DateTime? utc) =>
        utc is { } v ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(v, DateTimeKind.Utc), Dk) : null;

    private static string Format(object? v) => v switch
    {
        null => "",
        DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
        bool b => b ? "1" : "0",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };

    private static string Escape(string s) =>
        s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;

    private static TimeZoneInfo FindDk()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen"); }
        catch { return TimeZoneInfo.Utc; }
    }
}
