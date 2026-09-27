using Citra.Domain.Documents;

namespace Citra.Application.Common.Chunking;

/// <summary>
/// The context put in front of a chunk's text before it is embedded, so a chunk like "Up to 5 days carry over."
/// is still found for "how many vacation days carry over?":
/// <code>
/// Document: Employee Handbook
/// Section: Vacation › Carry-over
///
/// Up to 5 days carry over.
/// </code>
/// The one place that builds it: chunking and (re-)embedding must produce exactly the same text.
/// </summary>
public static class ChunkContext
{
    /// <summary>Longest title kept in the prefix (web page titles can be very long).</summary>
    public const int MaxTitleLength = 200;

    /// <summary>The text that is embedded for a chunk: context lines, a blank line, then the chunk's own text.</summary>
    public static string ForEmbedding(string? title, string? heading, string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var lines = new List<string>(2);
        if (Clean(title, MaxTitleLength) is { } documentTitle)
        {
            lines.Add($"Document: {documentTitle}");
        }

        if (Clean(heading, TextChunker.MaxHeadingLength) is { } section)
        {
            lines.Add($"Section: {section}");
        }

        return lines.Count == 0 ? content : $"{string.Join('\n', lines)}\n\n{content}";
    }

    /// <summary>
    /// The title shown in the prefix: the one found in the document (Word title, web page &lt;title&gt;) if any,
    /// otherwise the file name without its extension, or the address for web pages.
    /// </summary>
    public static string DocumentTitle(DocumentSourceType sourceType, string fileName, string? extractedTitle)
    {
        if (!string.IsNullOrWhiteSpace(extractedTitle))
        {
            return extractedTitle.Trim();
        }

        if (sourceType == DocumentSourceType.Url)
        {
            return fileName.Trim(); // "help.acme.com/returns": the extension trick would leave only "returns"
        }

        var withoutExtension = Path.GetFileNameWithoutExtension(fileName.Trim());
        return string.IsNullOrWhiteSpace(withoutExtension) ? fileName.Trim() : withoutExtension;
    }

    /// <summary>One line, trimmed, shortened with "…" if too long; null if empty.</summary>
    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var oneLine = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return oneLine.Length <= maxLength ? oneLine : oneLine[..(maxLength - 1)].TrimEnd() + "…";
    }
}
