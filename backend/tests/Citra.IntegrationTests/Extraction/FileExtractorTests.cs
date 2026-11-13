using System.Text;
using Citra.Application.Common.Extraction;
using Citra.Application.Common.Storage;
using Citra.Application.Features.Documents.Upload;
using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.Infrastructure.Extraction;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Citra.IntegrationTests.Extraction;

[Collection(DatabaseCollection.Name)]
public sealed class FileExtractorTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Stream-based tests never touch storage.
    private static readonly IFileStorage NoStorage = null!;

    // ---------- PDF ----------

    [Fact]
    public async Task Pdf_text_is_extracted_page_by_page()
    {
        var pdf = SampleFiles.Pdf(
            ["Returns Policy", "Items can be returned within 30 days."],
            ["Refunds are paid within 5 business days."]);

        var result = await new PdfTextExtractor(NoStorage).ExtractAsync(new MemoryStream(pdf), Ct);

        result.Pages.Select(p => p.PageNumber).ShouldBe([1, 2]);
        result.Pages[0].Text.ShouldContain("Returns Policy");
        result.Pages[0].Text.ShouldContain("Items can be returned within 30 days.");
        result.Pages[1].Text.ShouldContain("Refunds are paid within 5 business days.");
        result.Pages[0].Text.ShouldNotContain("Refunds", customMessage: "text must stay on its own page");
    }

    [Fact]
    public async Task Pdf_keeps_line_breaks_in_reading_order()
    {
        var pdf = SampleFiles.Pdf(["First line", "Second line", "Third line"]);

        var text = (await new PdfTextExtractor(NoStorage).ExtractAsync(new MemoryStream(pdf), Ct)).Pages[0].Text;

        text.IndexOf("First line").ShouldBeLessThan(text.IndexOf("Second line"));
        text.IndexOf("Second line").ShouldBeLessThan(text.IndexOf("Third line"));
        text.ShouldContain("\n");
    }

    [Fact]
    public async Task Scanned_pdf_is_rejected_with_a_clear_message()
    {
        var error = await Should.ThrowAsync<TextExtractionException>(
            () => new PdfTextExtractor(NoStorage).ExtractAsync(new MemoryStream(SampleFiles.ScannedPdf()), Ct));

        error.Message.ShouldBe(PdfTextExtractor.ScannedMessage);
        error.Message.ShouldContain("(OCR) is not supported yet");
    }

    [Fact]
    public async Task Pdf_without_text_or_images_says_so()
    {
        var error = await Should.ThrowAsync<TextExtractionException>(
            () => new PdfTextExtractor(NoStorage).ExtractAsync(new MemoryStream(SampleFiles.EmptyPdf()), Ct));

        error.Message.ShouldBe(PdfTextExtractor.NoTextMessage);
    }

    [Fact]
    public async Task A_damaged_pdf_gives_a_readable_error()
    {
        var damaged = Encoding.ASCII.GetBytes("%PDF-1.7\nthis is not a real pdf structure at all");

        var error = await Should.ThrowAsync<TextExtractionException>(
            () => new PdfTextExtractor(NoStorage).ExtractAsync(new MemoryStream(damaged), Ct));

        error.Message.ShouldContain("could not be read");
    }

    // ---------- Word ----------

    [Fact]
    public async Task Docx_keeps_headings_lists_and_tables()
    {
        var result = await new DocxTextExtractor(NoStorage).ExtractAsync(new MemoryStream(SampleFiles.Docx()), Ct);
        var page1 = result.Pages[0].Text;

        result.Title.ShouldBe("Employee Handbook");
        page1.ShouldContain("# Vacation\n");
        page1.ShouldContain("## Carry-over\n", customMessage: "a German 'Überschrift 2' style is still a level-2 heading");
        page1.ShouldContain("Employees get 25 vacation days per year.");
        page1.ShouldContain("- Up to 5 days carry over.");
        page1.ShouldContain("- Unused days expire in March.");
        page1.ShouldContain("Role | Days\nStaff | 25\nManager | 28");
    }

    [Fact]
    public async Task Docx_page_breaks_start_a_new_page_without_counting_twice()
    {
        var result = await new DocxTextExtractor(NoStorage).ExtractAsync(new MemoryStream(SampleFiles.Docx()), Ct);

        result.Pages.Count.ShouldBe(2);
        result.Pages[0].Text.ShouldContain("End of page one.");
        result.Pages[1].PageNumber.ShouldBe(2);
        result.Pages[1].Text.ShouldBe("Sick leave is paid from day one.\n\n");
    }

    [Fact]
    public async Task Empty_docx_has_no_text()
    {
        var result = await new DocxTextExtractor(NoStorage).ExtractAsync(new MemoryStream(SampleFiles.EmptyDocx()), Ct);

        result.HasText.ShouldBeFalse();
    }

    [Fact]
    public async Task A_zip_that_is_not_a_word_file_gives_a_readable_error()
    {
        var notWord = new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4, 5, 6 };

        var error = await Should.ThrowAsync<TextExtractionException>(
            () => new DocxTextExtractor(NoStorage).ExtractAsync(new MemoryStream(notWord), Ct));

        error.Message.ShouldContain("This Word file could not be read");
    }

    // ---------- Plain text and Markdown ----------

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8-bom")]
    [InlineData("utf16-bom")]
    [InlineData("windows-1250")]
    public async Task Text_files_are_decoded_in_common_encodings(string encoding)
    {
        const string text = "Radno vrijeme: ponedjeljak–petak. Čćžšđ ČĆŽŠĐ.";
        var bytes = encoding switch
        {
            "utf8" => Encoding.UTF8.GetBytes(text),
            "utf8-bom" => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)],
            "utf16-bom" => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)],
            _ => Windows1250(text),
        };

        var result = await new PlainTextExtractor(NoStorage).ExtractAsync(new MemoryStream(bytes), Ct);

        result.Pages.ShouldHaveSingleItem().Text.ShouldBe(encoding == "windows-1250" ? text.Replace('–', '-') : text);
    }

    [Fact]
    public async Task Markdown_is_kept_as_it_is()
    {
        const string markdown = "# FAQ\n\n## Shipping\n\n- Free over $50\n- 2 business days\n";

        var result = await new PlainTextExtractor(NoStorage).ExtractAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(markdown)), Ct);

        result.Pages.ShouldHaveSingleItem().Text.ShouldBe(markdown);
    }

    // ---------- Through storage and DI ----------

    [Theory]
    [InlineData("Handbook.pdf", DocumentSourceType.Pdf, "vacation days")]
    [InlineData("Handbook.docx", DocumentSourceType.Docx, "25 vacation days")]
    [InlineData("FAQ.md", DocumentSourceType.Markdown, "## Shipping")]
    public async Task Extracts_uploaded_documents_through_the_factory(string fileName, DocumentSourceType type, string expected)
    {
        var bytes = type switch
        {
            DocumentSourceType.Pdf => SampleFiles.Pdf(["Employees get 25 vacation days per year."]),
            DocumentSourceType.Docx => SampleFiles.Docx(),
            _ => Encoding.UTF8.GetBytes("# FAQ\n\n## Shipping\n\nWe ship within 2 business days.\n"),
        };
        var document = await UploadAsync(fileName, bytes);

        await using var scope = fixture.OpenScope(tenantId: null);
        var factory = scope.Services.GetRequiredService<ITextExtractorFactory>();
        var result = await factory.For(type).ExtractAsync(document, Ct);

        string.Concat(result.Pages.Select(p => p.Text)).ShouldContain(expected);
    }

    [Fact]
    public async Task A_file_missing_from_storage_is_reported()
    {
        var document = await UploadAsync("gone.pdf", SampleFiles.Pdf(["Some text that is long enough to count."]));
        File.Delete(Path.Combine(fixture.StorageRoot, document.StoragePath!));

        await using var scope = fixture.OpenScope(tenantId: null);
        var extractor = scope.Services.GetRequiredService<ITextExtractorFactory>().For(DocumentSourceType.Pdf);

        var error = await Should.ThrowAsync<TextExtractionException>(() => extractor.ExtractAsync(document, Ct));
        error.Message.ShouldContain("missing");
    }

    [Fact]
    public async Task The_factory_has_an_extractor_for_every_document_type()
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        var factory = scope.Services.GetRequiredService<ITextExtractorFactory>();

        foreach (var type in Enum.GetValues<DocumentSourceType>())
        {
            factory.For(type).SourceTypes.ShouldContain(type);
        }
    }

    [Fact]
    public void The_factory_refuses_two_extractors_for_one_type()
    {
        var error = Should.Throw<InvalidOperationException>(
            () => new TextExtractorFactory([new PlainTextExtractor(NoStorage), new PlainTextExtractor(NoStorage)]));

        error.Message.ShouldContain("PlainTextExtractor");
    }

    // ---------- helpers ----------

    private static byte[] Windows1250(string text)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1250).GetBytes(text.Replace('–', '-'));
    }

    /// <summary>Uploads through the real command, then loads the saved document.</summary>
    private async Task<Document> UploadAsync(string fileName, byte[] bytes)
    {
        var tenant = Tenant.Create("Extraction company");
        tenant.ChangeSlug($"extract-{Guid.NewGuid():N}");
        var bot = Bot.Create(tenant.Id, "Support");
        await using (var system = fixture.OpenScope(tenantId: null))
        {
            system.Db.AddRange(tenant, bot);
            await system.Db.SaveChangesAsync(Ct);
        }

        await using var scope = fixture.OpenScope(tenant.Id);
        var dto = await scope.Send(new UploadDocumentCommand(bot.Id, fileName, bytes.Length, new MemoryStream(bytes)));
        return scope.Db.Documents.Single(d => d.Id == dto.Id);
    }
}
