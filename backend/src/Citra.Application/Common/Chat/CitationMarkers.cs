using System.Text.RegularExpressions;

namespace Citra.Application.Common.Chat;

/// <summary>Finds which numbered sources an answer cites: "[1]", "[2][3]", and also "[1, 2]".</summary>
public static partial class CitationMarkers
{
    /// <summary>
    /// The cited sources, in number order, each once. Numbers no source has (a model mistake like [9] with six
    /// sources) are ignored: there is nothing to link them to.
    /// </summary>
    public static IReadOnlyList<NumberedSource> Cited(string answer, NumberedSources sources)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(sources);

        return Marker().Matches(answer)
            .SelectMany(m => m.Groups["numbers"].Value.Split(','))
            .Select(n => int.TryParse(n.Trim(), out var number) ? number : 0)
            .Distinct()
            .Select(sources.Find)
            .OfType<NumberedSource>()
            .OrderBy(s => s.Number)
            .ToList();
    }

    [GeneratedRegex(@"\[(?<numbers>\d{1,3}(?:\s*,\s*\d{1,3})*)\]")]
    private static partial Regex Marker();
}
