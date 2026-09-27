using System.Text;
using Citra.Application.Common.Chunking;
using Citra.Application.Common.Extraction;
using Citra.Infrastructure.Chunking;

namespace Citra.IntegrationTests.Chunking;

/// <summary>The chunker (C-125): structure-aware splitting within size limits, losing no text.</summary>
public sealed class TextChunkerTests
{
    private static readonly TiktokenTokenCounter Tokens = new();
    private static readonly TextChunker Chunker = new(Tokens);
    private static readonly ChunkingOptions Options = ChunkingOptions.Default;

    [Fact]
    public void An_empty_document_has_no_chunks()
    {
        Chunker.Chunk(Doc(""), hasPages: true).ShouldBeEmpty();
        Chunker.Chunk(Doc("   \n\n \t "), hasPages: true).ShouldBeEmpty();
        Chunker.Chunk(new ExtractedDocument([]), hasPages: true).ShouldBeEmpty();
    }

    [Fact]
    public void A_short_document_is_one_chunk()
    {
        const string text = "Items can be returned within 30 days.\n\nRefunds take 5 business days.";

        var chunk = Chunker.Chunk(Doc(text), hasPages: true).ShouldHaveSingleItem();

        chunk.Index.ShouldBe(0);
        chunk.Content.ShouldBe(text);
        chunk.TokenCount.ShouldBe(Tokens.Count(text));
        chunk.Heading.ShouldBeNull();
        chunk.PageNumber.ShouldBe(1);
        (chunk.CharStart, chunk.CharEnd).ShouldBe((0, text.Length));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(80)]
    public void Paragraphs_are_packed_up_to_the_target_and_never_split(int overlap)
    {
        var options = Options with { OverlapTokens = overlap };
        var paragraphs = Enumerable.Range(1, 60).Select(Paragraph).ToList();
        var document = Doc(string.Join("\n\n", paragraphs));

        var chunks = Chunker.Chunk(document, hasPages: true, options: options);

        chunks.Count.ShouldBeGreaterThan(3);
        AssertInvariants(document, chunks, options);
        chunks.ShouldAllBe(c => c.TokenCount <= Options.TargetTokens);
        foreach (var paragraph in paragraphs)
        {
            // Without overlap each paragraph is in exactly one chunk; with overlap it may also repeat in the next.
            var whole = chunks.Count(c => c.Content.Contains(paragraph));
            if (overlap == 0)
            {
                whole.ShouldBe(1, $"paragraph must be whole in one chunk: {paragraph[..30]}");
            }
            else
            {
                whole.ShouldBeGreaterThanOrEqualTo(1, $"paragraph must be whole in some chunk: {paragraph[..30]}");
            }
        }

        // Well packed: all but the last chunk are more than half full.
        chunks.SkipLast(1).ShouldAllBe(c => c.TokenCount > Options.TargetTokens / 2);
    }

    [Fact]
    public void A_paragraph_between_target_and_max_stays_whole()
    {
        var big = string.Join(" ", Enumerable.Repeat("This sentence is part of one long paragraph.", 60));
        Tokens.Count(big).ShouldBeInRange(Options.TargetTokens + 1, Options.MaxTokens);
        var document = Doc($"Intro paragraph.\n\n{big}\n\nOutro paragraph.");

        var chunks = Chunker.Chunk(document, hasPages: true);

        AssertInvariants(document, chunks);
        chunks.Count(c => c.Content == big).ShouldBe(1);
    }

    [Fact]
    public void A_paragraph_over_the_maximum_is_cut_at_spaces_into_pieces()
    {
        var huge = string.Join(" ", Enumerable.Range(1, 400).Select(i => $"Word{i} alpha beta gamma."));
        Tokens.Count(huge).ShouldBeGreaterThan(Options.MaxTokens);
        var document = Doc(huge);

        var chunks = Chunker.Chunk(document, hasPages: true);

        chunks.Count.ShouldBeGreaterThan(1);
        AssertInvariants(document, chunks);
        chunks.ShouldAllBe(c => c.TokenCount <= Options.TargetTokens);
        var text = document.Pages[0].Text;
        foreach (var chunk in chunks)
        {
            // Cut between words: every piece starts and ends on a word boundary.
            (chunk.CharStart == 0 || char.IsWhiteSpace(text[chunk.CharStart - 1])).ShouldBeTrue(chunk.Content[..20]);
            (chunk.CharEnd == text.Length || char.IsWhiteSpace(text[chunk.CharEnd])).ShouldBeTrue(chunk.Content[^20..]);
        }
    }

    [Fact]
    public void Every_heading_starts_a_new_chunk_and_names_its_section()
    {
        const string text = """
            # Employee Handbook

            Welcome to Acme.

            ## Vacation

            Employees get 25 vacation days.

            ### Carry-over

            Up to 5 days carry over.

            ## Sick leave

            Sick leave is paid from day one.
            """;
        var document = Doc(text);

        var chunks = Chunker.Chunk(document, hasPages: true);

        AssertInvariants(document, chunks);
        chunks.Select(c => (c.Heading, FirstLine(c.Content))).ShouldBe(
        [
            ("Employee Handbook", "# Employee Handbook"),
            ("Employee Handbook › Vacation", "## Vacation"),
            ("Employee Handbook › Vacation › Carry-over", "### Carry-over"),
            ("Employee Handbook › Sick leave", "## Sick leave"),
        ]);
        chunks[1].Content.ShouldBe("## Vacation\n\nEmployees get 25 vacation days.");
    }

    [Fact]
    public void Headings_directly_above_another_heading_stay_with_its_section()
    {
        var document = Doc("# Handbook\n\n## Vacation\n\n25 days per year.\n\n## Sick leave\n\nPaid from day one.");

        var chunks = Chunker.Chunk(document, hasPages: true);

        AssertInvariants(document, chunks);
        chunks.Count.ShouldBe(2, "\"# Handbook\" alone must not become a chunk");
        chunks[0].Content.ShouldBe("# Handbook\n\n## Vacation\n\n25 days per year.");
        chunks[0].Heading.ShouldBe("Handbook › Vacation");
        chunks[1].Heading.ShouldBe("Handbook › Sick leave");
    }

    [Fact]
    public void A_heading_with_text_on_the_next_line_is_one_block()
    {
        var document = Doc("## Shipping\nWe ship within 2 business days.\n\n## Returns\nWithin 30 days.");

        var chunks = Chunker.Chunk(document, hasPages: false);

        chunks.Select(c => c.Heading).ShouldBe(["Shipping", "Returns"]);
        chunks[0].Content.ShouldBe("## Shipping\nWe ship within 2 business days.");
    }

    [Fact]
    public void Lines_that_only_look_like_headings_are_ordinary_text()
    {
        var document = Doc("#hashtag is not a heading\n\n####### seven hashes neither\n\nPrice #5 is fine.");

        var chunk = Chunker.Chunk(document, hasPages: false).ShouldHaveSingleItem();

        chunk.Heading.ShouldBeNull();
    }

    [Fact]
    public void Chunks_never_cross_pages_and_the_section_carries_over()
    {
        var document = new ExtractedDocument(
        [
            new ExtractedPage(1, "# Returns\n\nItems can be returned within 30 days."),
            new ExtractedPage(2, "Refunds are paid within 5 business days."),
            new ExtractedPage(3, "   "),
            new ExtractedPage(4, "## Exceptions\n\nSale items cannot be returned."),
        ]);

        var chunks = Chunker.Chunk(document, hasPages: true);

        AssertInvariants(document, chunks);
        chunks.Select(c => (c.Index, c.PageNumber, c.Heading)).ShouldBe(
        [
            (0, 1, "Returns"),
            (1, 2, "Returns"),
            (2, 4, "Returns › Exceptions"),
        ]);
    }

    [Fact]
    public void Sources_without_pages_have_no_page_numbers()
    {
        var chunks = Chunker.Chunk(Doc(string.Join("\n\n", Enumerable.Range(1, 40).Select(Paragraph))), hasPages: false);

        chunks.Count.ShouldBeGreaterThan(1);
        chunks.ShouldAllBe(c => c.PageNumber == null);
    }

    [Fact]
    public void A_very_long_heading_path_is_shortened_to_fit_the_column()
    {
        var longTitle = new string('x', 200);
        var document = Doc($"# {longTitle}\n\n## {longTitle}\n\nBody text.");

        var chunk = Chunker.Chunk(document, hasPages: false).ShouldHaveSingleItem();

        chunk.Heading!.Length.ShouldBeLessThanOrEqualTo(TextChunker.MaxHeadingLength);
        chunk.Heading.ShouldEndWith("…");
    }

    [Fact]
    public void Rejects_nonsensical_sizes()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Chunker.Chunk(Doc("x"), true, options: new ChunkingOptions(TargetTokens: 5)));
        Should.Throw<ArgumentOutOfRangeException>(() => Chunker.Chunk(Doc("x"), true, options: new ChunkingOptions(500, MaxTokens: 400)));
    }

    // ---------- invariants every chunking must satisfy ----------

    /// <summary>
    /// Exact slices, sequential indexes, size limits, no blank chunks, and no text lost: every non-whitespace
    /// character of every page lies inside a chunk of that page.
    /// </summary>
    internal static void AssertInvariants(ExtractedDocument document, IReadOnlyList<TextChunk> chunks, ChunkingOptions? options = null)
    {
        options ??= ChunkingOptions.Default;
        chunks.Select(c => c.Index).ShouldBe(Enumerable.Range(0, chunks.Count));

        foreach (var chunk in chunks)
        {
            var page = document.Pages.Single(p => p.PageNumber == (chunk.PageNumber ?? p.PageNumber) && chunk.CharEnd <= p.Text.Length
                && p.Text[chunk.CharStart..chunk.CharEnd] == chunk.Content);
            chunk.Content.ShouldBe(page.Text[chunk.CharStart..chunk.CharEnd]);
            chunk.Content.ShouldBe(chunk.Content.Trim(), "no leading/trailing whitespace");
            chunk.Content.ShouldNotBeNullOrWhiteSpace();
            chunk.TokenCount.ShouldBe(Tokens.Count(chunk.Content));
            chunk.TokenCount.ShouldBeLessThanOrEqualTo(options.MaxTokens);
        }

        foreach (var page in document.Pages)
        {
            var covered = new bool[page.Text.Length];
            foreach (var chunk in chunks.Where(c => c.CharEnd <= page.Text.Length && page.Text[c.CharStart..c.CharEnd] == c.Content))
            {
                for (var i = chunk.CharStart; i < chunk.CharEnd; i++)
                {
                    covered[i] = true;
                }
            }

            for (var i = 0; i < page.Text.Length; i++)
            {
                if (!char.IsWhiteSpace(page.Text[i]))
                {
                    covered[i].ShouldBeTrue($"page {page.PageNumber} lost text at {i}: \"{Around(page.Text, i)}\"");
                }
            }
        }
    }

    private static string Around(string text, int i) => text[Math.Max(0, i - 20)..Math.Min(text.Length, i + 20)];

    internal static ExtractedDocument Doc(string text) => new([new ExtractedPage(1, text)]);

    internal static string Paragraph(int n) =>
        $"Paragraph {n}: employees at Acme receive clear guidance on policy number {n}, "
        + "including how to request time off, whom to contact, and which forms to submit before the deadline.";

    private static string FirstLine(string text) => text.Split('\n')[0];
}
