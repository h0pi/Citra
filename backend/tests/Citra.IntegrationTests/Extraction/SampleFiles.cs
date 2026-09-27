using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using PdfPageSize = UglyToad.PdfPig.Content.PageSize;

namespace Citra.IntegrationTests.Extraction;

/// <summary>
/// Small sample documents, built in code so each test's input is visible right next to it
/// (and no binary files are needed in the repository).
/// </summary>
internal static class SampleFiles
{
    /// <summary>A 1×1 pixel PNG, for "scanned" pages that are only an image.</summary>
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    /// <summary>A PDF with one page per entry; each page's lines are written top to bottom.</summary>
    public static byte[] Pdf(params string[][] pages) => Pdf(lineSpacing: 20, pages);

    /// <summary>Same, with a custom distance between lines (12 fits about 62 lines on an A4 page).</summary>
    public static byte[] Pdf(double lineSpacing, params string[][] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var lines in pages)
        {
            var page = builder.AddPage(PdfPageSize.A4);
            var y = 800.0;
            foreach (var line in lines)
            {
                page.AddText(line, 9, new PdfPoint(40, y), font);
                y -= lineSpacing;
            }
        }

        return builder.Build();
    }

    /// <summary>A PDF whose pages are only pictures, like a scanned paper document.</summary>
    public static byte[] ScannedPdf(int pages = 2)
    {
        var builder = new PdfDocumentBuilder();
        for (var i = 0; i < pages; i++)
        {
            builder.AddPage(PdfPageSize.A4).AddPng(Png, new PdfRectangle(0, 0, 595, 842));
        }

        return builder.Build();
    }

    /// <summary>A PDF with no content at all.</summary>
    public static byte[] EmptyPdf()
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(PdfPageSize.A4);
        return builder.Build();
    }

    /// <summary>
    /// A Word file: document title, an English "Heading1" and a German "Überschrift2" style (headings by outline
    /// level, not by name), a list, a table, an explicit page break and a Word "rendered page break" right after
    /// it (which must not count as a second page).
    /// </summary>
    public static byte[] Docx()
    {
        using var stream = new MemoryStream();
        using (var word = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            word.PackageProperties.Title = "Employee Handbook";
            var main = word.AddMainDocumentPart();

            var styles = main.AddNewPart<StyleDefinitionsPart>();
            styles.Styles = new Styles(
                HeadingStyle("Heading1", "heading 1", outlineLevel: 0),
                HeadingStyle("berschrift2", "Überschrift 2", outlineLevel: 1));

            main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(new Body(
                StyledParagraph("Heading1", "Vacation"),
                Paragraph("Employees get 25 vacation days per year."),
                StyledParagraph("berschrift2", "Carry-over"),
                ListParagraph("Up to 5 days carry over."),
                ListParagraph("Unused days expire in March."),
                new Table(
                    Row("Role", "Days"),
                    Row("Staff", "25"),
                    Row("Manager", "28")),
                new Paragraph(new Run(new Text("End of page one.")), new Run(new Break { Type = BreakValues.Page })),
                new Paragraph(new Run(new LastRenderedPageBreak(), new Text("Sick leave is paid from day one.")))));
        }

        return stream.ToArray();
    }

    /// <summary>A Word file whose body is empty.</summary>
    public static byte[] EmptyDocx()
    {
        using var stream = new MemoryStream();
        using (var word = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            word.AddMainDocumentPart().Document = new DocumentFormat.OpenXml.Wordprocessing.Document(new Body());
        }

        return stream.ToArray();
    }

    private static Style HeadingStyle(string id, string name, int outlineLevel) =>
        new(new StyleName { Val = name }, new StyleParagraphProperties(new OutlineLevel { Val = outlineLevel }))
        {
            Type = StyleValues.Paragraph,
            StyleId = id,
        };

    private static Paragraph Paragraph(string text) => new(new Run(new Text(text)));

    private static Paragraph StyledParagraph(string styleId, string text) =>
        new(new ParagraphProperties(new ParagraphStyleId { Val = styleId }), new Run(new Text(text)));

    private static Paragraph ListParagraph(string text) =>
        new(
            new ParagraphProperties(new NumberingProperties(
                new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 1 })),
            new Run(new Text(text)));

    private static TableRow Row(params string[] cells) =>
        new(cells.Select(c => new TableCell(Paragraph(c))).Cast<OpenXmlElement>());
}
