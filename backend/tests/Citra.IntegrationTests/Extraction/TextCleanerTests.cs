using Citra.Application.Common.Extraction;
using Citra.Infrastructure.Extraction;

namespace Citra.IntegrationTests.Extraction;

/// <summary>Text cleanup (C-120).</summary>
public sealed class TextCleanerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("infor-\nmation", "information")]            // word split at the end of a line
    [InlineData("Covid-\n19", "Covid-\n19")]                  // not between two lower-case letters: kept
    [InlineData("e-\nMail", "e-\nMail")]
    [InlineData("well-known", "well-known")]                  // a real hyphen inside a line: kept
    public void Rejoins_words_split_across_lines(string input, string expected) =>
        TextCleaner.Normalize(input).ShouldBe(expected);

    [Fact]
    public void Removes_invisible_characters_and_fixes_spacing()
    {
        var messy = "Price:  10 EUR\r\n\r\n\r\n\r\nSoft­hyphen and zero​width.\t\tTabs.  \n   Indented\u0007 bell.";

        TextCleaner.Normalize(messy).ShouldBe("Price: 10 EUR\n\nSofthyphen and zerowidth. Tabs.\nIndented bell.");
    }

    [Fact]
    public void Replaces_typographic_ligatures()
    {
        TextCleaner.Normalize("The ﬁrst oﬀice ﬂow").ShouldBe("The first office flow");
    }

    [Fact]
    public void Removes_running_headers_and_page_numbers_but_keeps_page_numbering()
    {
        var pages = Enumerable.Range(1, 4)
            .Select(n => new ExtractedPage(n, $"ACME Employee Handbook\nSection {n} text.\nMore about topic {n}.\nPage {n} of 4"))
            .ToList();

        var cleaned = TextCleaner.Clean(new ExtractedDocument(pages));

        cleaned.Pages.Select(p => p.PageNumber).ShouldBe([1, 2, 3, 4]);
        cleaned.Pages[2].Text.ShouldBe("Section 3 text.\nMore about topic 3.");
    }

    [Fact]
    public void Keeps_repeated_lines_in_short_documents()
    {
        var pages = new[] { new ExtractedPage(1, "Title\nBody one."), new ExtractedPage(2, "Title\nBody two.") };

        TextCleaner.Clean(new ExtractedDocument(pages)).Pages[0].Text.ShouldBe("Title\nBody one.");
    }

    [Fact]
    public void Keeps_a_number_at_the_end_of_a_single_page_text()
    {
        var single = new ExtractedDocument([new ExtractedPage(1, "Prices valid until\n2027")]);

        TextCleaner.Clean(single).Pages[0].Text.ShouldBe("Prices valid until\n2027");
    }

    [Fact]
    public void Keeps_lines_that_only_repeat_on_a_few_pages()
    {
        var pages = Enumerable.Range(1, 5)
            .Select(n => new ExtractedPage(n, n <= 2 ? $"Warning\nPage body {n}." : $"Chapter {n}\nPage body {n}."))
            .ToList();

        var cleaned = TextCleaner.Clean(new ExtractedDocument(pages));

        cleaned.Pages[0].Text.ShouldStartWith("Warning");
    }

    [Fact]
    public async Task Cleans_real_pdf_output_end_to_end()
    {
        var pdf = SampleFiles.Pdf(Enumerable.Range(1, 4)
            .Select(n => new[]
            {
                "ACME Employee Handbook",
                $"Chapter {n}: all employees receive the infor-",
                "mation they need on day one.",
                $"Page {n} of 4",
            })
            .ToArray());

        var extracted = await new PdfTextExtractor(null!).ExtractAsync(new MemoryStream(pdf), Ct);
        var cleaned = TextCleaner.Clean(extracted);

        // Header and "Page 2 of 4" gone, "infor-/mation" rejoined, body lines ("Chapter 2") kept.
        cleaned.Pages[1].Text.ShouldBe(
            "Chapter 2: all employees receive the information they need on day one.", customMessage: CleanedFor(cleaned));
    }

    private static string CleanedFor(ExtractedDocument document) =>
        "cleaned page was: " + string.Join(" || ", document.Pages.Select(p => p.Text.Replace("\n", "\\n")));
}
