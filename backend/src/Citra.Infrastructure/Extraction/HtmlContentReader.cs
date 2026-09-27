using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Citra.Infrastructure.Extraction;

/// <summary>
/// Turns a web page into readable text: keeps the main content (&lt;main&gt;, &lt;article&gt; or the body),
/// drops navigation, headers, footers, sidebars, forms, cookie banners and scripts, and keeps structure the
/// chunker understands ("# Heading", "- item", "cell | cell").
/// </summary>
internal static class HtmlContentReader
{
    /// <summary>Never content: code, media, forms, and page chrome that repeats on every page of a site.</summary>
    private const string NoiseSelector =
        "script, style, noscript, template, svg, canvas, iframe, object, embed, video, audio, picture, img, "
        + "form, button, input, select, textarea, dialog, "
        + "nav, aside, "
        + "[role=navigation], [role=banner], [role=contentinfo], [role=complementary], [role=search], "
        + "[aria-hidden=true], [hidden], "
        + "[id*=cookie i], [class*=cookie i], [id*=consent i], [class*=consent i]";

    private static readonly HashSet<string> BlockTags =
    [
        "P", "DIV", "SECTION", "ARTICLE", "MAIN", "BLOCKQUOTE", "PRE", "UL", "OL", "DL", "DT", "DD",
        "TABLE", "FIGURE", "FIGCAPTION", "ADDRESS", "DETAILS", "SUMMARY", "HR",
    ];

    public static (string Text, string? Title) Read(byte[] html, string? charset)
    {
        var parser = new HtmlParser();

        // AngleSharp detects the encoding from <meta charset>; the HTTP header's charset wins if present.
        using var stream = new MemoryStream(html);
        using var document = charset is null
            ? parser.ParseDocument(stream)
            : parser.ParseDocument(PlainTextExtractor.Decode(html, charset));

        var title = TitleOf(document);

        foreach (var noise in document.QuerySelectorAll(NoiseSelector).ToList())
        {
            noise.Remove();
        }

        // Site-wide header/footer go; an article's own <header> (its title, date) stays.
        foreach (var chrome in document.QuerySelectorAll("header, footer").ToList())
        {
            if (chrome.Closest("main, article, [role=main]") is null)
            {
                chrome.Remove();
            }
        }

        var root = MainContent(document);
        if (root is null)
        {
            return (string.Empty, title);
        }

        var text = new StringBuilder();
        Write(root, text);
        return (text.ToString(), title);
    }

    private static string? TitleOf(IDocument document)
    {
        var title = document.QuerySelector("meta[property='og:title']")?.GetAttribute("content")
            ?? document.Title;
        return string.IsNullOrWhiteSpace(title) ? null : title.Trim();
    }

    /// <summary>The element holding the page's real content: &lt;main&gt;, else the biggest &lt;article&gt;, else &lt;body&gt;.</summary>
    private static IElement? MainContent(IDocument document) =>
        document.QuerySelector("main, [role=main]")
        ?? document.QuerySelectorAll("article").MaxBy(a => a.TextContent.Length)
        ?? document.Body;

    private static void Write(INode node, StringBuilder text)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child)
            {
                case IText textNode:
                    text.Append(textNode.Data);
                    break;

                case IElement element:
                    WriteElement(element, text);
                    break;
            }
        }
    }

    private static void WriteElement(IElement element, StringBuilder text)
    {
        switch (element.TagName)
        {
            case "H1" or "H2" or "H3" or "H4" or "H5" or "H6":
                var level = element.TagName[1] - '0';
                BlankLine(text);
                text.Append(new string('#', level)).Append(' ').Append(Flatten(element)).Append("\n\n");
                return;

            case "LI":
                NewLine(text);
                text.Append("- ");
                Write(element, text);
                text.Append('\n');
                return;

            case "BR":
                text.Append('\n');
                return;

            case "TR":
                var cells = element.Children
                    .Where(c => c.TagName is "TD" or "TH")
                    .Select(Flatten)
                    .ToList();
                if (cells.Any(c => c.Length > 0))
                {
                    NewLine(text);
                    text.Append(string.Join(" | ", cells)).Append('\n');
                }

                return;

            case "PRE":
                BlankLine(text);
                text.Append(element.TextContent.Trim('\n')).Append("\n\n");
                return;
        }

        var isBlock = BlockTags.Contains(element.TagName);
        if (isBlock)
        {
            BlankLine(text);
        }

        Write(element, text);

        if (isBlock)
        {
            BlankLine(text);
        }
    }

    /// <summary>An element's text on one line (headings, table cells).</summary>
    private static string Flatten(IElement element) =>
        string.Join(' ', element.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static void NewLine(StringBuilder text)
    {
        if (text.Length > 0 && text[^1] != '\n')
        {
            text.Append('\n');
        }
    }

    private static void BlankLine(StringBuilder text)
    {
        NewLine(text);
        if (text.Length > 1 && text[^2] != '\n')
        {
            text.Append('\n');
        }
    }
}
