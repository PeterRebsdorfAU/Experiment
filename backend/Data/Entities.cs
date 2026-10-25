namespace AiExperiment.Api.Data;

/// <summary>Optimistisk låsning: samtidige ændringer af samme række afvises i stedet for at overskrive hinanden.</summary>
public interface IVersioned { int Version { get; set; } }

public static class Groups
{
    public const string High = "high";
    public const string Low = "low";
}

/// <summary>Trinene i deltagerens forløb, i fast rækkefølge. Man kan kun gå fremad.</summary>
public static class Steps
{
    public const string Instructions = "instructions";
    public const string Notice = "notice";
    public const string Task = "task";
    public const string Manipulation = "manipulation";
    public const string Background = "background";
    public const string Debrief = "debrief";
}

/// <summary>Globale indstillinger (altid én række med Id = 1).</summary>
public class Settings
{
    public int Id { get; set; } = 1;
    /// <summary>Når false, kan nye deltagere ikke starte (de venter på "Start" fra admin).</summary>
    public bool StudyOpen { get; set; }
    /// <summary>Antal rigtige svar der kræves for at deltage i lodtrækningen.</summary>
    public int MinCorrectForLottery { get; set; } = 3;
}

public class Question
{
    public int Id { get; set; }
    public int SortOrder { get; set; }
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public decimal? CorrectAnswer { get; set; }
    /// <summary>Hvor meget svaret må afvige fra det korrekte (fx 0,5 ved afrunding).</summary>
    public decimal Tolerance { get; set; } = 0.01m;
    /// <summary>Den forudskrevne tekst, der vises når man trykker "Get AI help".</summary>
    public string AiResponse { get; set; } = "";
    /// <summary>Det tal, AI'en giver som svar. Bruges til at se om deltageren kopierede AI'en.</summary>
    public decimal? AiAnswerValue { get; set; }
    /// <summary>Slettede spørgsmål arkiveres, så allerede indsamlede data ikke mister deres spørgsmål.</summary>
    public bool Archived { get; set; }
}

public class Participant : IVersioned
{
    public int Version { get; set; }
    public Guid Id { get; set; }
    /// <summary>Kort anonymt ID, der står i alle eksporter (fx P-7K3F9Q).</summary>
    public string Code { get; set; } = "";
    public bool IsTest { get; set; }
    public string? Group { get; set; }
    /// <summary>Løbenummer i randomiseringen (kun ikke-test-deltagere).</summary>
    public int? AssignmentIndex { get; set; }
    public string Step { get; set; } = Steps.Instructions;
    /// <summary>Spørgsmåls-ID'er låst ved fordelingen, så ændringer i admin ikke påvirker igangværende deltagere.</summary>
    public string QuestionIds { get; set; } = "";
    public int TaskIndex { get; set; }

    public DateTime ConsentAt { get; set; }
    public DateTime? AssignedAt { get; set; }
    public DateTime? TasksStartedAt { get; set; }
    public DateTime? TasksEndedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public int? ManipulationCheck { get; set; }
    public string? Gender { get; set; }
    public string? AiUsageFrequency { get; set; }
    public bool LotterySubmitted { get; set; }

    public List<TaskResponse> Tasks { get; set; } = [];

    public int[] GetQuestionIds() =>
        QuestionIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
}

/// <summary>Én række pr. deltager pr. opgave.</summary>
public class TaskResponse : IVersioned
{
    public int Version { get; set; }
    public long Id { get; set; }
    public Guid ParticipantId { get; set; }
    public Participant? Participant { get; set; }
    public int QuestionId { get; set; }
    public int Position { get; set; }

    public DateTime ShownAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public long? TimeSpentMs { get; set; }

    public int AiClicks { get; set; }
    public DateTime? FirstAiClickAt { get; set; }

    public string? AnswerRaw { get; set; }
    public decimal? AnswerValue { get; set; }
    public bool? IsCorrect { get; set; }
    public bool? MatchesAi { get; set; }
}

/// <summary>Rå hændelseslog: hvert klik/tastetryk/fanebytte med tidsstempel.</summary>
public class EventLog
{
    public long Id { get; set; }
    public Guid ParticipantId { get; set; }
    /// <summary>Klientens ID for hændelsen, så et genforsøg efter netværksfejl ikke giver dubletter.</summary>
    public string? ClientEventId { get; set; }
    public int? QuestionId { get; set; }
    public string Type { get; set; } = "";
    public DateTime ServerTime { get; set; }
    public DateTime? ClientTime { get; set; }
    public string? Data { get; set; }
}

/// <summary>
/// Tekster rettet på admin-siden (fx samtykke og instruktion). Kun ændrede tekster gemmes;
/// resten tager standardteksten fra frontend/src/app/texts.ts.
/// </summary>
public class TextSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>
/// E-mails til lodtrækningen. Bevidst UDEN kobling til deltageren og kun med dato (ikke klokkeslæt),
/// så en e-mail ikke kan matches med et bestemt sæt svar.
/// </summary>
public class LotteryEntry
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public bool Eligible { get; set; }
    public DateOnly SubmittedOn { get; set; }
}
