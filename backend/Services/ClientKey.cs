namespace AiExperiment.Api.Services;

public static class RateLimits
{
    public const string CreateParticipant = "create-participant";
}

/// <summary>
/// Nøgle til at begrænse antal forespørgsler pr. afsender. Bruger den sidste adresse i X-Forwarded-For
/// (sat af Renders proxy og ikke af klienten, så den ikke kan forfalskes). IP-adressen bruges kun
/// midlertidigt i hukommelsen til tællingen og gemmes aldrig i databasen eller i logs.
/// </summary>
public static class ClientKey
{
    public static string For(HttpContext ctx)
    {
        var forwarded = ctx.Request.Headers["X-Forwarded-For"].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return forwarded.LastOrDefault() ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
