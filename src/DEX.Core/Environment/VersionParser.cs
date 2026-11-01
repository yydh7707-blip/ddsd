using System.Text.RegularExpressions;

namespace DEX.Core.Probes;

public static class VersionParser
{
    private static readonly Regex VersionRegex = new(@"\d+\.\d+(?:\.\d+)?(?:\.\d+)?", RegexOptions.Compiled);

    public static string? Extract(string text)
    {
        var match = VersionRegex.Match(text);
        return match.Success ? match.Value : null;
    }

    public static bool Satisfies(string? installed, string? requirement)
    {
        if (string.IsNullOrWhiteSpace(requirement))
            return true;
        if (string.IsNullOrWhiteSpace(installed))
            return false;
        if (!Version.TryParse(Normalize(installed), out var have))
            return false;

        var spec = requirement.Trim();
        if (spec.StartsWith(">=", StringComparison.Ordinal))
            return Version.TryParse(Normalize(spec[2..]), out var min) && have >= min;
        if (spec.StartsWith('>'))
            return Version.TryParse(Normalize(spec[1..]), out var min) && have > min;
        if (spec.StartsWith("==", StringComparison.Ordinal))
            return Version.TryParse(Normalize(spec[2..]), out var exact) && have == exact;
        if (spec.StartsWith('='))
            return Version.TryParse(Normalize(spec[1..]), out var exact) && have == exact;
        if (Version.TryParse(Normalize(spec), out var majorPinned))
        {
            if (spec.Count(c => c == '.') == 0)
                return have.Major == majorPinned.Major;
            return have.Major == majorPinned.Major && (spec.Count(c => c == '.') < 1 || have.Minor == majorPinned.Minor || spec.EndsWith(".0") && have.Major == majorPinned.Major);
        }

        return have.ToString().StartsWith(spec, StringComparison.Ordinal);
    }

    public static string Normalize(string value)
    {
        var extracted = Extract(value) ?? value.Trim();
        var parts = extracted.Split('.');
        return parts.Length switch
        {
            1 => extracted + ".0",
            _ => extracted
        };
    }

    public static int CompareMajor(string? installed, int requiredMajor)
    {
        if (!Version.TryParse(Normalize(installed ?? ""), out var have))
            return -1;
        return have.Major.CompareTo(requiredMajor);
    }
}
