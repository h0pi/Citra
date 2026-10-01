using System.Text;
using Citra.Application.Common.Retrieval;

namespace Citra.Application.Common.Chat;

/// <summary>
/// Numbers the passages found for a question and writes them for the model:
/// <code>
/// &lt;sources&gt;
/// &lt;source id="1" document="Handbook.pdf" page="4" section="Benefits › Vacation"&gt;
/// Employees get 25 days of paid vacation.
/// &lt;/source&gt;
/// &lt;/sources&gt;
/// </code>
/// The numbers are what the answer cites as [1], [2]; <see cref="NumberedSources.Sources"/> maps them back to the
/// chunks. Document text is untrusted: it is escaped, so a document cannot close a tag or fake another source.
/// </summary>
public static class SourceList
{
    public const string NoSources = "(No matching passages were found in the documents.)";

    public static NumberedSources Number(IReadOnlyList<RetrievedChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        var numbered = chunks.Select((chunk, i) => new NumberedSource(i + 1, chunk)).ToList();
        var block = new StringBuilder("<sources>\n");
        if (numbered.Count == 0)
        {
            block.Append(NoSources).Append('\n');
        }

        foreach (var (number, chunk) in numbered)
        {
            block.Append($"<source id=\"{number}\" document=\"{Escape(chunk.DocumentName)}\"");
            if (chunk.PageNumber is { } page)
            {
                block.Append($" page=\"{page}\"");
            }

            if (!string.IsNullOrWhiteSpace(chunk.Heading))
            {
                block.Append($" section=\"{Escape(chunk.Heading)}\"");
            }

            block.Append(">\n").Append(Escape(chunk.Content.Trim())).Append("\n</source>\n");
        }

        block.Append("</sources>");
        return new NumberedSources(block.ToString(), numbered);
    }

    /// <summary>XML escaping for text and attribute values (&amp; &lt; &gt; &quot;).</summary>
    public static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>The reverse of <see cref="Escape"/>.</summary>
    public static string Unescape(string text) =>
        text.Replace("&quot;", "\"").Replace("&gt;", ">").Replace("&lt;", "<").Replace("&amp;", "&");
}

/// <summary>The sources block for the prompt, and which chunk each number stands for.</summary>
public sealed record NumberedSources(string Block, IReadOnlyList<NumberedSource> Sources)
{
    /// <summary>The source cited as [<paramref name="number"/>], or null for a number that was never given.</summary>
    public NumberedSource? Find(int number) => Sources.FirstOrDefault(s => s.Number == number);
}

/// <summary>A passage and the number the model cites it by.</summary>
public sealed record NumberedSource(int Number, RetrievedChunk Chunk);
