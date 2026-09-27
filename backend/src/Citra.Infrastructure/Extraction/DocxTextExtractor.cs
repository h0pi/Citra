using System.Text;
using System.Text.RegularExpressions;
using Citra.Application.Common.Extraction;
using Citra.Application.Common.Storage;
using Citra.Domain.Documents;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Citra.Infrastructure.Extraction;

/// <summary>
/// Reads Word (.docx) files in reading order: headings become "# Heading" lines (the chunker splits on them),
/// list items "- item", tables "cell | cell" rows. Word has no fixed pages, but it records where it broke pages
/// when the file was last saved; those breaks and explicit page breaks start a new page, so citations can still
/// say "page 3".
/// </summary>
internal sealed partial class DocxTextExtractor(IFileStorage storage) : FileTextExtractor(storage)
{
    public override IReadOnlyCollection<DocumentSourceType> SourceTypes { get; } = [DocumentSourceType.Docx];

    protected override string FormatName => "Word";

    protected override async Task<ExtractedDocument> ExtractCoreAsync(Stream content, CancellationToken cancellationToken)
    {
        await using var buffer = await BufferAsync(content, cancellationToken);
        using var word = WordprocessingDocument.Open(buffer, isEditable: false);

        var mainPart = word.MainDocumentPart;
        var body = mainPart?.Document?.Body
            ?? throw new TextExtractionException("This Word file has no content.");

        var writer = new PageWriter(HeadingLevels(mainPart.StyleDefinitionsPart));
        writer.WriteBlocks(body, cancellationToken);

        var title = word.PackageProperties.Title;
        return new ExtractedDocument(writer.Pages(), string.IsNullOrWhiteSpace(title) ? null : title.Trim());
    }

    /// <summary>
    /// Heading level per paragraph style id, from each style's outline level (following "based on" chains).
    /// Works for any language, e.g. a German "Überschrift1" style still is level 1.
    /// </summary>
    private static Dictionary<string, int> HeadingLevels(StyleDefinitionsPart? stylesPart)
    {
        var styles = stylesPart?.Styles?.Elements<Style>()
            .Where(s => s.StyleId?.Value is not null)
            .GroupBy(s => s.StyleId!.Value!)
            .ToDictionary(g => g.Key, g => g.First())
            ?? [];

        int? LevelOf(Style style, int depth)
        {
            if (style.StyleParagraphProperties?.OutlineLevel?.Val?.Value is { } outline && outline < 9)
            {
                return outline + 1;
            }

            if (style.StyleId?.Value is { } id && (id.Equals("Title", StringComparison.OrdinalIgnoreCase)))
            {
                return 1;
            }

            if (style.StyleId?.Value is { } styleId && HeadingStyleId().Match(styleId) is { Success: true } match)
            {
                return int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }

            return depth < 10 && style.BasedOn?.Val?.Value is { } parent && styles.TryGetValue(parent, out var parentStyle)
                ? LevelOf(parentStyle, depth + 1)
                : null;
        }

        return styles
            .Select(pair => (pair.Key, Level: LevelOf(pair.Value, 0)))
            .Where(x => x.Level is not null)
            .ToDictionary(x => x.Key, x => Math.Min(x.Level!.Value, 6));
    }

    [GeneratedRegex(@"^heading\s?([1-9])$", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingStyleId();

    /// <summary>Writes blocks as text into pages, starting a new page at every page break.</summary>
    private sealed class PageWriter(Dictionary<string, int> headingLevels)
    {
        private readonly List<StringBuilder> _pages = [new()];

        private StringBuilder Current => _pages[^1];

        public IReadOnlyList<ExtractedPage> Pages() =>
            _pages.Select((text, index) => new ExtractedPage(index + 1, text.ToString())).ToList();

        public void WriteBlocks(OpenXmlElement container, CancellationToken cancellationToken)
        {
            foreach (var element in container.ChildElements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (element)
                {
                    case Paragraph paragraph:
                        WriteParagraph(paragraph);
                        break;
                    case Table table:
                        WriteTable(table);
                        break;
                    default:
                        // Content controls, custom XML blocks, ...: their paragraphs are inside.
                        if (element.HasChildren)
                        {
                            WriteBlocks(element, cancellationToken);
                        }

                        break;
                }
            }
        }

        private void WriteParagraph(Paragraph paragraph)
        {
            var properties = paragraph.ParagraphProperties;
            var prefix = string.Empty;
            if (properties?.ParagraphStyleId?.Val?.Value is { } styleId && headingLevels.TryGetValue(styleId, out var level))
            {
                prefix = new string('#', level) + " ";
            }
            else if (properties?.OutlineLevel?.Val?.Value is { } outline && outline < 6)
            {
                prefix = new string('#', outline + 1) + " ";
            }
            else if (properties?.NumberingProperties is not null)
            {
                prefix = "- ";
            }

            var line = new StringBuilder();
            foreach (var node in paragraph.Descendants())
            {
                switch (node)
                {
                    case Text text:
                        line.Append(text.Text);
                        break;
                    case TabChar:
                        line.Append('\t');
                        break;
                    case Break { Type.Value: var type } when type == BreakValues.Page:
                    case LastRenderedPageBreak:
                        // A heading that starts on the new page keeps its "#"; only text after the break loses it.
                        if (FlushLine(prefix, line))
                        {
                            prefix = string.Empty;
                        }

                        // Word often records the same page change twice (an explicit break, then a "rendered
                        // here" marker at the start of the next paragraph): a new page only after some text.
                        if (Current.Length > 0)
                        {
                            _pages.Add(new StringBuilder());
                        }

                        break;
                    case Break:
                    case CarriageReturn:
                        line.Append('\n');
                        break;
                }
            }

            FlushLine(prefix, line);
        }

        /// <summary>Writes the collected text (if any) as one paragraph. Returns whether anything was written.</summary>
        private bool FlushLine(string prefix, StringBuilder line)
        {
            var text = line.ToString().Trim();
            line.Clear();
            if (text.Length == 0)
            {
                return false;
            }

            Current.Append(prefix).Append(text).Append("\n\n");
            return true;
        }

        private void WriteTable(Table table)
        {
            foreach (var row in table.Elements<TableRow>())
            {
                var cells = row.Elements<TableCell>()
                    .Select(cell => string.Join(" ", cell.Descendants<Text>().Select(t => t.Text)).Trim());
                var line = string.Join(" | ", cells);
                if (line.Replace("|", string.Empty).Trim().Length > 0)
                {
                    Current.Append(line).Append('\n');
                }
            }

            Current.Append('\n');
        }
    }
}
