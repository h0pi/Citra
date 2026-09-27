using System.Text;
using System.Text.RegularExpressions;
using Citra.Application.Common.Chunking;
using Citra.Application.Common.Extraction;
using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.Infrastructure.Chunking;
using Citra.Infrastructure.Extraction;
using Citra.IntegrationTests.Extraction;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Citra.IntegrationTests.Chunking.TextChunkerTests;

namespace Citra.IntegrationTests.Chunking;

/// <summary>
/// Page numbers and character offsets (C-127): every chunk must point at exactly its passage on the right page,
/// through the real pipeline (extract → clean → chunk) and after saving to the database.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed partial class ChunkOffsetsTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TiktokenTokenCounter Tokens = new();
    private static readonly TextChunker Chunker = new(Tokens);

    [Theory]
    [InlineData(DocumentSourceType.Pdf, true)]
    [InlineData(DocumentSourceType.Docx, true)]
    [InlineData(DocumentSourceType.Text, false)]
    [InlineData(DocumentSourceType.Markdown, false)]
    [InlineData(DocumentSourceType.Url, false)]
    public void Only_pdf_and_word_chunks_have_page_numbers(DocumentSourceType type, bool hasPages)
    {
        TextChunker.HasPages(type).ShouldBe(hasPages);
        Enum.GetValues<DocumentSourceType>().Length.ShouldBe(5, "a new source type must be classified here too");
    }

    [Fact]
    public async Task Pdf_chunks_point_at_their_passage_on_the_right_page()
    {
        // 3 pages × 60 lines; each line carries a unique marker "P{page}L{line}" (~850 tokens per page).
        var pdf = SampleFiles.Pdf(lineSpacing: 12, Enumerable.Range(1, 3)
            .Select(page => Enumerable.Range(1, 60)
                .Select(line => $"P{page}L{line:00} employees must read section {line} of the handbook carefully.")
                .ToArray())
            .ToArray());

        var document = TextCleaner.Clean(await new PdfTextExtractor(null!).ExtractAsync(new MemoryStream(pdf), Ct));
        var chunks = Chunker.Chunk(document, TextChunker.HasPages(DocumentSourceType.Pdf));

        AssertInvariants(document, chunks);
        chunks.Count.ShouldBeGreaterThan(3, "each page is bigger than one chunk");
        chunks.Select(c => c.PageNumber).Distinct().ShouldBe([1, 2, 3]);
        foreach (var chunk in chunks)
        {
            var page = document.Pages[chunk.PageNumber!.Value - 1];
            chunk.Content.ShouldBe(page.Text[chunk.CharStart..chunk.CharEnd]);

            var markers = Marker().Matches(chunk.Content).Select(m => m.Value).ToList();
            markers.ShouldNotBeEmpty();
            markers.ShouldAllBe(m => m.StartsWith($"P{chunk.PageNumber}L"), "a chunk holds text of its own page only");
        }

        // Every line of every page ends up in some chunk of that page.
        for (var page = 1; page <= 3; page++)
        {
            for (var line = 1; line <= 60; line++)
            {
                var marker = $"P{page}L{line:00}";
                chunks.ShouldContain(c => c.PageNumber == page && c.Content.Contains(marker), marker);
            }
        }
    }

    [Fact]
    public async Task Word_chunks_follow_page_breaks()
    {
        var document = TextCleaner.Clean(await new DocxTextExtractor(null!).ExtractAsync(new MemoryStream(SampleFiles.Docx()), Ct));

        var chunks = Chunker.Chunk(document, TextChunker.HasPages(DocumentSourceType.Docx));

        AssertInvariants(document, chunks);
        chunks.Single(c => c.Content.Contains("25 vacation days")).PageNumber.ShouldBe(1);
        var sickLeave = chunks.Single(c => c.Content.Contains("Sick leave is paid"));
        sickLeave.PageNumber.ShouldBe(2);
        sickLeave.Heading.ShouldBe("Vacation › Carry-over", "the section continues on the next page");
        (sickLeave.CharStart, sickLeave.CharEnd).ShouldBe((0, document.Pages[1].Text.Length));
    }

    [Fact]
    public async Task Markdown_chunks_have_no_page_but_exact_offsets_into_the_whole_text()
    {
        var markdown = "# FAQ\n\n" + string.Join("\n\n", Enumerable.Range(1, 40)
            .Select(i => $"## Question {i}\n\nAnswer {i}: our support team replies within one business day, even on holidays."));
        var document = TextCleaner.Clean(
            await new PlainTextExtractor(null!).ExtractAsync(new MemoryStream(Encoding.UTF8.GetBytes(markdown)), Ct));

        var chunks = Chunker.Chunk(document, TextChunker.HasPages(DocumentSourceType.Markdown));

        AssertInvariants(document, chunks);
        chunks.ShouldAllBe(c => c.PageNumber == null);
        var text = document.Pages.ShouldHaveSingleItem().Text;
        foreach (var chunk in chunks)
        {
            text[chunk.CharStart..chunk.CharEnd].ShouldBe(chunk.Content);
        }

        chunks.Single(c => c.Heading == "FAQ › Question 17").Content.ShouldContain("Answer 17:");
    }

    [Fact]
    public void Offsets_count_like_javascript_even_with_emoji_and_accents()
    {
        // "😀" is 2 UTF-16 units (as in JavaScript), "Č" is 1: offsets must slice exactly, in C# and in the browser.
        var paragraphs = Enumerable.Range(1, 30)
            .Select(i => $"Odgovor {i} 😀: radno vrijeme je od 8 do 16 sati, Čćžšđ, osim praznika 👍🏽.");
        var document = Doc(string.Join("\n\n", paragraphs));

        var chunks = Chunker.Chunk(document, hasPages: false);

        AssertInvariants(document, chunks);
        chunks.Count.ShouldBeGreaterThan(1);
        var text = document.Pages[0].Text;
        foreach (var chunk in chunks)
        {
            text.Substring(chunk.CharStart, chunk.CharEnd - chunk.CharStart).ShouldBe(chunk.Content);
            char.IsLowSurrogate(text[chunk.CharStart]).ShouldBeFalse("never starts inside an emoji");
            char.IsHighSurrogate(text[chunk.CharEnd - 1]).ShouldBeFalse("never ends inside an emoji");
        }
    }

    [Fact]
    public async Task Chunks_are_saved_and_read_back_with_the_same_pages_and_offsets()
    {
        var tenant = Tenant.Create("Chunk company");
        tenant.ChangeSlug($"chunks-{Guid.NewGuid():N}");
        var bot = Bot.Create(tenant.Id, "Support");
        var documentEntity = Document.FromUpload(
            tenant.Id, bot.Id, "Handbook.pdf", "application/pdf", 100, DocumentSourceType.Pdf,
            $"{tenant.Id}/{bot.Id}/{Guid.NewGuid():N}/original.pdf", new string('a', 64));

        var extracted = new ExtractedDocument(
        [
            new ExtractedPage(1, "# Handbook\n\n" + string.Join("\n\n", Enumerable.Range(1, 30).Select(Paragraph))),
            new ExtractedPage(2, "## Appendix\n\nForms are available from HR. 😀"),
        ]);
        var chunks = Chunker.Chunk(extracted, hasPages: true);

        await using (var system = fixture.OpenScope(tenantId: null))
        {
            system.Db.AddRange(tenant, bot, documentEntity);
            system.Db.Chunks.AddRange(chunks.Select(c => c.ToEntity(documentEntity.Id, bot.Id)));
            await system.Db.SaveChangesAsync(Ct);
        }

        await using var check = fixture.OpenScope(tenantId: null);
        var saved = await check.Db.Chunks
            .Where(c => c.DocumentId == documentEntity.Id)
            .OrderBy(c => c.Index)
            .ToListAsync(Ct);

        saved.Count.ShouldBe(chunks.Count);
        foreach (var (original, stored) in chunks.Zip(saved))
        {
            stored.BotId.ShouldBe(bot.Id);
            (stored.Index, stored.Content, stored.Heading, stored.PageNumber, stored.CharStart, stored.CharEnd, stored.TokenCount)
                .ShouldBe((original.Index, original.Content, original.Heading, original.PageNumber, original.CharStart, original.CharEnd, original.TokenCount));
            stored.Embedding.ShouldBeNull("embeddings are added in the next step");
        }

        saved[^1].PageNumber.ShouldBe(2);
        saved[^1].Heading.ShouldBe("Handbook › Appendix");
    }

    [GeneratedRegex(@"P\d+L\d{2}")]
    private static partial Regex Marker();
}
