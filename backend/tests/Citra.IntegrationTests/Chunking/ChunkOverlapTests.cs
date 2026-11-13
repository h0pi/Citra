using Citra.Application.Common.Chunking;
using Citra.Application.Common.Extraction;
using Citra.Infrastructure.Chunking;
using static Citra.IntegrationTests.Chunking.TextChunkerTests;

namespace Citra.IntegrationTests.Chunking;

/// <summary>Overlap between consecutive chunks.</summary>
public sealed class ChunkOverlapTests
{
    private static readonly TiktokenTokenCounter Tokens = new();
    private static readonly TextChunker Chunker = new(Tokens);
    private static readonly ChunkingOptions Options = ChunkingOptions.Default;

    [Fact]
    public void Consecutive_chunks_share_about_80_tokens_starting_at_a_word()
    {
        var document = Doc(string.Join("\n\n", Enumerable.Range(1, 60).Select(Paragraph)));
        var text = document.Pages[0].Text;

        var chunks = Chunker.Chunk(document, hasPages: true);

        AssertInvariants(document, chunks);
        foreach (var (previous, next) in chunks.Zip(chunks.Skip(1)))
        {
            next.CharStart.ShouldBeLessThan(previous.CharEnd, "the next chunk repeats the end of the previous one");
            next.CharStart.ShouldBeGreaterThan(previous.CharStart, "but never all of it");
            next.CharEnd.ShouldBeGreaterThan(previous.CharEnd, "and always brings new text");

            var shared = Tokens.Count(text[next.CharStart..previous.CharEnd]);
            shared.ShouldBeInRange(Options.OverlapTokens / 2, Options.OverlapTokens);
            char.IsWhiteSpace(text[next.CharStart - 1]).ShouldBeTrue("overlap starts at a whole word");
        }
    }

    [Fact]
    public void Pieces_of_an_oversized_paragraph_overlap_too()
    {
        var huge = string.Join(" ", Enumerable.Range(1, 400).Select(i => $"Word{i} alpha beta gamma."));
        var document = Doc(huge);

        var chunks = Chunker.Chunk(document, hasPages: true);

        AssertInvariants(document, chunks);
        chunks.Count.ShouldBeGreaterThan(2);
        chunks.ShouldAllBe(c => c.TokenCount <= Options.TargetTokens);
        foreach (var (previous, next) in chunks.Zip(chunks.Skip(1)))
        {
            next.CharStart.ShouldBeInRange(previous.CharStart + 1, previous.CharEnd - 1);
            Tokens.Count(huge[next.CharStart..previous.CharEnd]).ShouldBeInRange(Options.OverlapTokens / 2, Options.OverlapTokens);
        }
    }

    [Fact]
    public void No_overlap_into_a_new_section()
    {
        var body = string.Join("\n\n", Enumerable.Range(1, 12).Select(Paragraph));
        var document = Doc($"## Vacation\n\n{body}\n\n## Sick leave\n\n{body}");
        var text = document.Pages[0].Text;

        var chunks = Chunker.Chunk(document, hasPages: true);

        AssertInvariants(document, chunks);
        var sickLeave = chunks.First(c => c.Heading == "Sick leave");
        sickLeave.CharStart.ShouldBe(text.IndexOf("## Sick leave", StringComparison.Ordinal));
        sickLeave.Content.ShouldStartWith("## Sick leave");
    }

    [Fact]
    public void No_overlap_onto_a_new_page()
    {
        var body = string.Join("\n\n", Enumerable.Range(1, 12).Select(Paragraph));
        var document = new ExtractedDocument([new ExtractedPage(1, body), new ExtractedPage(2, body)]);

        var chunks = Chunker.Chunk(document, hasPages: true);

        AssertInvariants(document, chunks);
        chunks.First(c => c.PageNumber == 2).CharStart.ShouldBe(0);
    }

    [Fact]
    public void Overlap_can_be_turned_off()
    {
        var document = Doc(string.Join("\n\n", Enumerable.Range(1, 60).Select(Paragraph)));
        var options = Options with { OverlapTokens = 0 };

        var chunks = Chunker.Chunk(document, hasPages: true, options: options);

        AssertInvariants(document, chunks, options);
        foreach (var (previous, next) in chunks.Zip(chunks.Skip(1)))
        {
            next.CharStart.ShouldBeGreaterThanOrEqualTo(previous.CharEnd);
        }
    }

    [Fact]
    public void A_short_chunk_is_never_repeated_whole()
    {
        // A tiny paragraph followed by one too big to share its chunk.
        var big = string.Join(" ", Enumerable.Repeat("This sentence is part of one long paragraph.", 60));
        var document = Doc($"Short intro.\n\n{big}");

        var chunks = Chunker.Chunk(document, hasPages: true);

        AssertInvariants(document, chunks);
        chunks.Count.ShouldBe(2);
        chunks[1].Content.ShouldNotContain("Short intro.");
    }

    [Fact]
    public void Overlap_is_skipped_when_it_would_exceed_the_maximum()
    {
        // The second paragraph alone is close to the maximum; adding 80 tokens of overlap would exceed it.
        var first = string.Join(" ", Enumerable.Repeat("The first paragraph talks about returns and refunds.", 25));
        var second = string.Join(" ", Enumerable.Repeat("The second paragraph is nearly as long as a chunk may be.", 60));
        Tokens.Count(second).ShouldBeInRange(Options.MaxTokens - Options.OverlapTokens + 1, Options.MaxTokens);
        var document = Doc($"{first}\n\n{second}");

        var chunks = Chunker.Chunk(document, hasPages: true);

        AssertInvariants(document, chunks);
        chunks.Count.ShouldBe(2);
        chunks[1].Content.ShouldBe(second, "no overlap: it would not fit");
        chunks.ShouldAllBe(c => c.TokenCount <= Options.MaxTokens);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(251)]
    public void Rejects_overlap_that_would_leave_too_little_new_text(int overlap)
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => Chunker.Chunk(Doc("x"), hasPages: true, options: Options with { OverlapTokens = overlap }));
    }
}
