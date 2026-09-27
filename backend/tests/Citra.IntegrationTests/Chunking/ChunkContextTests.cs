using Citra.Application.Common.Chunking;
using Citra.Application.Common.Extraction;
using Citra.Domain.Documents;
using Citra.Infrastructure.Chunking;
using Citra.Infrastructure.Extraction;
using Citra.IntegrationTests.Extraction;
using static Citra.IntegrationTests.Chunking.TextChunkerTests;

namespace Citra.IntegrationTests.Chunking;

/// <summary>Document title and section heading in front of every chunk for embedding (C-129).</summary>
public sealed class ChunkContextTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TiktokenTokenCounter Tokens = new();
    private static readonly TextChunker Chunker = new(Tokens);

    [Theory]
    [InlineData("Employee Handbook", "Vacation › Carry-over", "Document: Employee Handbook\nSection: Vacation › Carry-over\n\nUp to 5 days.")]
    [InlineData("Employee Handbook", null, "Document: Employee Handbook\n\nUp to 5 days.")]
    [InlineData(null, "Vacation", "Section: Vacation\n\nUp to 5 days.")]
    [InlineData(null, null, "Up to 5 days.")]
    [InlineData("  ", " ", "Up to 5 days.")]
    [InlineData("Employee\n  Handbook\t2026", "Vacation", "Document: Employee Handbook 2026\nSection: Vacation\n\nUp to 5 days.")]
    public void Builds_the_embedding_text(string? title, string? heading, string expected) =>
        ChunkContext.ForEmbedding(title, heading, "Up to 5 days.").ShouldBe(expected);

    [Fact]
    public void Shortens_a_very_long_title()
    {
        var text = ChunkContext.ForEmbedding(new string('t', 500), null, "Body.");
        var titleLine = text.Split('\n')[0];

        titleLine.Length.ShouldBe("Document: ".Length + ChunkContext.MaxTitleLength);
        titleLine.ShouldEndWith("…");
    }

    [Fact]
    public void The_prefix_stays_small_even_at_the_longest()
    {
        var longest = ChunkContext.ForEmbedding(
            string.Join(" ", Enumerable.Repeat("Title words", 100)),
            string.Join(" › ", Enumerable.Repeat("Section name", 60)),
            "Body.");

        (Tokens.Count(longest) - Tokens.Count("Body.")).ShouldBeLessThanOrEqualTo(150);
    }

    [Theory]
    [InlineData(DocumentSourceType.Pdf, "Employee Handbook.pdf", null, "Employee Handbook")]
    [InlineData(DocumentSourceType.Docx, "Report.v2.final.docx", null, "Report.v2.final")]
    [InlineData(DocumentSourceType.Markdown, "README", null, "README")]
    [InlineData(DocumentSourceType.Pdf, ".pdf", null, ".pdf")]
    [InlineData(DocumentSourceType.Url, "help.acme.com/returns", null, "help.acme.com/returns")]
    [InlineData(DocumentSourceType.Url, "help.acme.com/returns", "  Returns | Acme Help ", "Returns | Acme Help")]
    [InlineData(DocumentSourceType.Docx, "hb.docx", "Employee Handbook", "Employee Handbook")]
    public void Picks_the_best_document_title(DocumentSourceType type, string fileName, string? extracted, string expected) =>
        ChunkContext.DocumentTitle(type, fileName, extracted).ShouldBe(expected);

    [Fact]
    public void Every_chunk_carries_its_title_and_section()
    {
        const string text = "# Handbook\n\nWelcome.\n\n## Vacation\n\nEmployees get 25 days.\n\n## Sick leave\n\nPaid from day one.";
        var document = Doc(text);

        var chunks = Chunker.Chunk(document, hasPages: true, title: "Employee Handbook");

        AssertInvariants(document, chunks);
        foreach (var chunk in chunks)
        {
            chunk.EmbeddingText.ShouldBe(ChunkContext.ForEmbedding("Employee Handbook", chunk.Heading, chunk.Content));
            chunk.EmbeddingText.ShouldStartWith("Document: Employee Handbook\nSection: Handbook");
            chunk.EmbeddingText.ShouldEndWith("\n\n" + chunk.Content);
        }

        chunks.Single(c => c.Content.Contains("25 days")).EmbeddingText
            .ShouldStartWith("Document: Employee Handbook\nSection: Handbook › Vacation\n\n");
    }

    [Fact]
    public void Without_a_title_or_heading_the_embedding_text_is_the_content()
    {
        var chunk = Chunker.Chunk(Doc("Just a short note."), hasPages: false).ShouldHaveSingleItem();

        chunk.EmbeddingText.ShouldBe("Just a short note.");
    }

    [Fact]
    public async Task A_word_file_gets_its_real_title_and_sections_end_to_end()
    {
        var extracted = await new DocxTextExtractor(null!).ExtractAsync(new MemoryStream(SampleFiles.Docx()), Ct);
        var document = TextCleaner.Clean(extracted);
        var title = ChunkContext.DocumentTitle(DocumentSourceType.Docx, "hb-final-v3.docx", document.Title);

        var chunks = Chunker.Chunk(document, TextChunker.HasPages(DocumentSourceType.Docx), title);

        title.ShouldBe("Employee Handbook", "the title stored in the Word file wins over the file name");
        chunks.Single(c => c.Content.Contains("25 vacation days")).EmbeddingText
            .ShouldStartWith("Document: Employee Handbook\nSection: Vacation\n\n");
        chunks.Single(c => c.Content.Contains("Sick leave")).EmbeddingText
            .ShouldStartWith("Document: Employee Handbook\nSection: Vacation › Carry-over\n\n");
    }

    [Fact]
    public void The_embedding_text_is_not_stored_and_can_be_rebuilt_from_a_saved_chunk()
    {
        var chunk = Chunker.Chunk(Doc("## Vacation\n\nEmployees get 25 days."), hasPages: true, title: "Handbook").Single();
        var entity = chunk.ToEntity(Guid.NewGuid(), Guid.NewGuid());

        entity.Content.ShouldBe(chunk.Content, "the stored text is the exact page slice, without the prefix");
        ChunkContext.ForEmbedding("Handbook", entity.Heading, entity.Content).ShouldBe(chunk.EmbeddingText);
    }
}
