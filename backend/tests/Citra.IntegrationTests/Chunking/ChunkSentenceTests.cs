using Citra.Application.Common.Chunking;
using Citra.Application.Common.Extraction;
using Citra.Infrastructure.Chunking;
using static Citra.IntegrationTests.Chunking.TextChunkerTests;

namespace Citra.IntegrationTests.Chunking;

/// <summary>Chunks begin and end on whole sentences.</summary>
public sealed class ChunkSentenceTests
{
    private static readonly TiktokenTokenCounter Tokens = new();
    private static readonly TextChunker Chunker = new(Tokens);
    private static readonly ChunkingOptions Options = ChunkingOptions.Default;

    [Fact]
    public void A_long_paragraph_is_split_between_sentences()
    {
        var sentences = Enumerable.Range(1, 120).Select(i =>
            $"Rule {i} explains how employees request leave, e.g. by form, and who approves it within {i % 7 + 1} days.");
        var document = Doc(string.Join(" ", sentences));

        var chunks = Chunker.Chunk(document, hasPages: true);

        AssertInvariants(document, chunks);
        chunks.Count.ShouldBeGreaterThan(3);
        AssertWholeSentences(document.Pages[0].Text, chunks);
        chunks.ShouldAllBe(c => c.TokenCount <= Options.TargetTokens);
    }

    [Fact]
    public void A_pdf_page_with_wrapped_lines_is_split_between_sentences()
    {
        // Like PdfPig output: one line per printed line, sentences wrap across lines, no blank lines at all.
        var words = string.Join(" ", Enumerable.Range(1, 90).Select(i =>
            $"Sentence number {i} describes the safety rule for warehouse zone {i} in plain words."));
        var wrapped = string.Join("\n", Chunk(words.Split(' '), 11).Select(line => string.Join(" ", line)));
        var document = new ExtractedDocument([new ExtractedPage(1, wrapped)]);

        var chunks = Chunker.Chunk(document, hasPages: true);

        AssertInvariants(document, chunks);
        chunks.Count.ShouldBeGreaterThan(1);
        AssertWholeSentences(wrapped, chunks);
    }

    [Fact]
    public void A_sentence_crossing_the_target_is_kept_whole_up_to_the_maximum()
    {
        // Short sentences up to ~430 tokens, then one ~250-token sentence that would cross the 500 target.
        var shortOnes = string.Join(" ", Enumerable.Range(1, 40).Select(i => $"Short sentence {i} is here."));
        var longOne = "This long sentence " + string.Join(" ", Enumerable.Repeat("keeps adding detail about the policy", 40)) + ".";
        var tail = string.Join(" ", Enumerable.Range(1, 80).Select(i => $"Closing sentence {i} ends it."));
        var document = Doc($"{shortOnes} {longOne} {tail}");

        var chunks = Chunker.Chunk(document, hasPages: true);

        AssertInvariants(document, chunks);
        AssertWholeSentences(document.Pages[0].Text, chunks);
        chunks.Count(c => c.Content.Contains(longOne)).ShouldBeGreaterThanOrEqualTo(1, "the long sentence is never cut");
    }

    [Fact]
    public void Only_a_sentence_longer_than_the_maximum_is_cut_and_then_at_words()
    {
        var endless = "This one sentence never ends " + string.Join(" ", Enumerable.Repeat("and goes on", 400));
        Tokens.Count(endless).ShouldBeGreaterThan(Options.MaxTokens);
        var document = Doc(endless);

        var chunks = Chunker.Chunk(document, hasPages: true);

        AssertInvariants(document, chunks);
        chunks.Count.ShouldBeGreaterThan(1);
        chunks.ShouldAllBe(c => c.TokenCount <= Options.MaxTokens);
        var text = document.Pages[0].Text;
        foreach (var chunk in chunks)
        {
            (chunk.CharEnd == text.Length || char.IsWhiteSpace(text[chunk.CharEnd])).ShouldBeTrue("cut between words");
        }
    }

    [Fact]
    public void Overlap_starts_at_a_sentence()
    {
        var sentences = Enumerable.Range(1, 150).Select(i => $"Fact {i}: returns are free within {i} days of delivery.");
        var document = Doc(string.Join(" ", sentences));
        var text = document.Pages[0].Text;

        var chunks = Chunker.Chunk(document, hasPages: true);

        AssertInvariants(document, chunks);
        foreach (var (previous, next) in chunks.Zip(chunks.Skip(1)))
        {
            next.CharStart.ShouldBeLessThan(previous.CharEnd, "chunks still overlap");
            next.Content.ShouldStartWith("Fact ", customMessage: "overlap begins with a whole sentence");
            Tokens.Count(text[next.CharStart..previous.CharEnd]).ShouldBeLessThanOrEqualTo(2 * Options.OverlapTokens);
        }
    }

    [Fact]
    public void Abbreviations_and_numbered_lists_are_not_cut_points()
    {
        var sentences = Enumerable.Range(1, 120).Select(i =>
            $"Dr. Smith checks item No. {i} at 3.5 bar, i.e. the normal pressure, and signs as J. Smith.");
        var document = Doc(string.Join(" ", sentences));

        var chunks = Chunker.Chunk(document, hasPages: true);

        AssertInvariants(document, chunks);
        chunks.ShouldAllBe(c => c.Content.StartsWith("Dr. Smith"), "a chunk never starts after an abbreviation or initial");
    }

    [Fact]
    public void A_long_list_is_split_between_items()
    {
        var items = Enumerable.Range(1, 150).Select(i => $"- Item {i}: bring your badge and wear safety shoes in zone {i}");
        var document = Doc("Rules for the warehouse:\n" + string.Join("\n", items));

        var chunks = Chunker.Chunk(document, hasPages: true);

        AssertInvariants(document, chunks);
        chunks.Count.ShouldBeGreaterThan(1);
        chunks.Skip(1).ShouldAllBe(c => c.Content.StartsWith("- Item "), "each piece starts with a whole item");
        chunks.ShouldAllBe(c => c.Content.Split('\n').Last().Contains("in zone"), "and ends with a whole item");
    }

    [Fact]
    public void A_huge_block_is_chunked_in_linear_time()
    {
        // A 1 MB text file without blank lines: one giant block, split into ~500 pieces.
        var text = string.Join(" ", Enumerable.Range(0, 17_000).Select(i => $"Sentence {i} explains one rule of the handbook in detail."));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var chunks = Chunker.Chunk(Doc(text), hasPages: false);

        stopwatch.Stop();
        chunks.Count.ShouldBeGreaterThan(400);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10), "a big upload must not stall the Worker");
    }

    // ---------- helpers ----------

    /// <summary>Every chunk starts at a sentence start (or its block's start) and ends right after a sentence end.</summary>
    private static void AssertWholeSentences(string text, IReadOnlyList<TextChunk> chunks)
    {
        var starts = SentenceBoundaries.Starts(text, 0, text.Length).ToHashSet();
        foreach (var chunk in chunks)
        {
            (chunk.CharStart == 0 || starts.Contains(chunk.CharStart))
                .ShouldBeTrue($"chunk {chunk.Index} starts mid-sentence: \"{chunk.Content[..40]}\"");
            (chunk.CharEnd == text.Length || starts.Contains(SkipWhitespace(text, chunk.CharEnd)))
                .ShouldBeTrue($"chunk {chunk.Index} ends mid-sentence: \"{chunk.Content[^40..]}\"");
        }
    }

    private static int SkipWhitespace(string text, int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }

        return position;
    }

    private static IEnumerable<T[]> Chunk<T>(T[] items, int size)
    {
        for (var i = 0; i < items.Length; i += size)
        {
            yield return items[i..Math.Min(items.Length, i + size)];
        }
    }
}
