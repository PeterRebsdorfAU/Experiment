using System.Globalization;
using System.Text.RegularExpressions;

namespace AiExperiment.Api.Services;

/// <summary>
/// Fortolker talsvar skrevet på dansk eller engelsk: "1.250,5", "1250.5", "1 250", "20 %", "4.000 kr".
/// </summary>
public static partial class AnswerParser
{
    public static decimal? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().ToLowerInvariant()
            .Replace("kr.", "").Replace("kr", "").Replace("dkk", "").Replace("%", "")
            .Replace(" ", "").Replace(" ", "").Replace("−", "-");
        if (s.Length == 0) return null;

        var hasDot = s.Contains('.');
        var hasComma = s.Contains(',');
        if (hasDot && hasComma)
        {
            // Det sidste tegn af de to er decimaltegnet.
            s = s.LastIndexOf(',') > s.LastIndexOf('.')
                ? s.Replace(".", "").Replace(",", ".")
                : s.Replace(",", "");
        }
        else if (hasComma)
        {
            s = ThousandsComma().IsMatch(s) ? s.Replace(",", "") : s.Replace(",", ".");
        }
        else if (hasDot && ThousandsDot().IsMatch(s))
        {
            s = s.Replace(".", ""); // "4.000" = fire tusinde (dansk skrivemåde)
        }

        return decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public static bool Matches(decimal? value, decimal? target, decimal tolerance) =>
        value.HasValue && target.HasValue && Math.Abs(value.Value - target.Value) <= tolerance;

    [GeneratedRegex(@"^-?\d{1,3}(\.\d{3})+$")]
    private static partial Regex ThousandsDot();

    [GeneratedRegex(@"^-?\d{1,3}(,\d{3}){2,}$")]
    private static partial Regex ThousandsComma();
}
