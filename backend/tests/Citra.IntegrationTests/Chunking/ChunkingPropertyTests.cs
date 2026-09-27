using System.Text;
using Citra.Application.Common.Chunking;
using Citra.Application.Common.Extraction;
using Citra.Infrastructure.Chunking;

namespace Citra.IntegrationTests.Chunking;

/// <summary>
/// C-130: every rule of chunking, checked together on 300 generated documents of every shape (headings, lists,
/// tables, long and short sentences, abbreviations, emoji, Bosnian text, blank and multiple pages, rare giant
/// sentences). Seeds are fixed, so a failure names the exact document to reproduce.
/// </summary>
public sealed class ChunkingPropertyTests
{
    private static readonly TiktokenTokenCounter Tokens = new();
    private static readonly TextChunker Chunker = new(Tokens);
    private static readonly ChunkingOptions Options = ChunkingOptions.Default;

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var batch = 0; batch < 30; batch++)
        {
            data.Add(batch);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Every_rule_holds_for_generated_documents(int batch)
    {
        // 30 batches × 10 documents = 300 documents.
        for (var seed = batch * 10; seed < (batch * 10) + 10; seed++)
        {
            var (document, title) = Generate(seed);
            var chunks = Chunker.Chunk(document, hasPages: true, title);

            AssertAllRules(document, title, chunks, $"seed {seed}");

            // Pageless sources: the very same chunks, only without page numbers.
            var pageless = Chunker.Chunk(document, hasPages: false, title);
            pageless.Select(c => c with { PageNumber = 0 }).ShouldBe(chunks.Select(c => c with { PageNumber = 0 }), $"seed {seed}");
            pageless.ShouldAllBe(c => c.PageNumber == null);

            // Deterministic: the same input always gives the same chunks.
            Chunker.Chunk(document, hasPages: true, title).ShouldBe(chunks, $"seed {seed}");
        }
    }

    private static void AssertAllRules(ExtractedDocument document, string title, IReadOnlyList<TextChunk> chunks, string because)
    {
        chunks.Select(c => c.Index).ShouldBe(Enumerable.Range(0, chunks.Count), because);
        var pages = document.Pages.ToDictionary(p => p.PageNumber);

        foreach (var chunk in chunks)
        {
            var where = $"{because}, chunk {chunk.Index}";

            // Page numbers and offsets: the content is exactly that slice of that page.
            chunk.PageNumber.ShouldNotBeNull(where);
            var text = pages[chunk.PageNumber!.Value].Text;
            chunk.CharStart.ShouldBeInRange(0, chunk.CharEnd - 1, where);
            chunk.CharEnd.ShouldBeLessThanOrEqualTo(text.Length, where);
            chunk.Content.ShouldBe(text[chunk.CharStart..chunk.CharEnd], where);

            // No empty chunks; sizes.
            chunk.Content.ShouldNotBeNullOrWhiteSpace(where);
            chunk.Content.ShouldBe(chunk.Content.Trim(), where);
            chunk.TokenCount.ShouldBe(Tokens.Count(chunk.Content), where);
            chunk.TokenCount.ShouldBeLessThanOrEqualTo(Options.MaxTokens, where);

            // Context for embedding.
            (chunk.Heading?.Length ?? 0).ShouldBeLessThanOrEqualTo(TextChunker.MaxHeadingLength, where);
            chunk.EmbeddingText.ShouldBe(ChunkContext.ForEmbedding(title, chunk.Heading, chunk.Content), where);
        }

        // Reading order, and overlap rules between neighbours.
        foreach (var (previous, next) in chunks.Zip(chunks.Skip(1)))
        {
            var where = $"{because}, chunks {previous.Index}→{next.Index}";
            next.PageNumber!.Value.ShouldBeGreaterThanOrEqualTo(previous.PageNumber!.Value, where);
            if (next.PageNumber != previous.PageNumber)
            {
                continue;
            }

            next.CharStart.ShouldBeGreaterThan(previous.CharStart, where);
            next.CharEnd.ShouldBeGreaterThan(previous.CharEnd, $"{where}: every chunk brings new text");
            if (next.CharStart < previous.CharEnd)
            {
                next.Heading.ShouldBe(previous.Heading, $"{where}: overlap stays within one section");
                var shared = pages[next.PageNumber!.Value].Text[next.CharStart..previous.CharEnd];
                Tokens.Count(shared).ShouldBeLessThanOrEqualTo(2 * Options.OverlapTokens, where);
            }
        }

        // Nothing lost: every non-whitespace character of every page is inside a chunk of that page.
        foreach (var page in document.Pages)
        {
            var covered = new bool[page.Text.Length];
            foreach (var chunk in chunks.Where(c => c.PageNumber == page.PageNumber))
            {
                Array.Fill(covered, true, chunk.CharStart, chunk.CharEnd - chunk.CharStart);
            }

            for (var i = 0; i < page.Text.Length; i++)
            {
                if (!char.IsWhiteSpace(page.Text[i]) && !covered[i])
                {
                    throw new ShouldAssertException($"{because}: page {page.PageNumber} lost text at {i}: " +
                        $"\"{page.Text[Math.Max(0, i - 20)..Math.Min(page.Text.Length, i + 20)]}\"");
                }
            }
        }
    }

    // ---------- document generator ----------

    private static readonly string[] Words =
    [
        "employees", "handbook", "policy", "vacation", "leave", "request", "manager", "approval", "form", "deadline",
        "safety", "warehouse", "shift", "overtime", "salary", "benefits", "training", "equipment", "customer", "refund",
        "zaposleni", "godišnji", "odmor", "pravilnik", "čuvar", "šef", "žalba", "đak", "ćelija", "rok",
    ];

    private static readonly string[] Openers =
    [
        "Dr. Smith says", "According to No. 4,", "In 2026", "Every employee", "The manager, e.g. Ana,", "As J. Doe noted,",
        "Prema pravilniku, npr.", "At 3.5 bar", "Customers 😀", "The team 👍🏽",
    ];

    private static (ExtractedDocument Document, string Title) Generate(int seed)
    {
        var random = new Random(seed);
        var pages = new List<ExtractedPage>();
        var pageCount = random.Next(1, 5);
        for (var page = 1; page <= pageCount; page++)
        {
            pages.Add(new ExtractedPage(page, random.Next(12) == 0 ? "  \n " : Page(random)));
        }

        var title = random.Next(4) == 0 ? new string('T', random.Next(150, 400)) : $"Document {seed}";
        return (new ExtractedDocument(pages), title);
    }

    private static string Page(Random random)
    {
        var blocks = new List<string>();
        var blockCount = random.Next(1, 25);
        for (var b = 0; b < blockCount; b++)
        {
            blocks.Add(random.Next(10) switch
            {
                0 or 1 => $"{new string('#', random.Next(1, 4))} {Sentence(random, 2, 6).TrimEnd('.')}",
                2 => string.Join("\n", Enumerable.Range(1, random.Next(3, 60)).Select(i => $"- Item {i}: {Sentence(random, 3, 12)}")),
                3 => string.Join("\n", Enumerable.Range(1, random.Next(3, 50)).Select(i => $"Row {i} | {Word(random)} | {random.Next(1000)}")),
                4 when random.Next(8) == 0 => "Endless " + string.Join(" ", Enumerable.Range(0, random.Next(700, 1200)).Select(_ => Word(random))),
                5 => Wrapped(random, string.Join(" ", Enumerable.Range(0, random.Next(5, 60)).Select(_ => Sentence(random, 5, 25)))),
                _ => string.Join(" ", Enumerable.Range(0, random.Next(1, 15)).Select(_ => Sentence(random, 5, 30))),
            });
        }

        return string.Join("\n\n", blocks);
    }

    /// <summary>Like PDF text: the same words broken into short lines at arbitrary places.</summary>
    private static string Wrapped(Random random, string text)
    {
        var words = text.Split(' ');
        var builder = new StringBuilder();
        for (var i = 0; i < words.Length; i++)
        {
            builder.Append(words[i]).Append(i < words.Length - 1 ? (random.Next(9) == 0 ? "\n" : " ") : "");
        }

        return builder.ToString();
    }

    private static string Sentence(Random random, int minWords, int maxWords)
    {
        var words = Enumerable.Range(0, random.Next(minWords, maxWords)).Select(_ => Word(random));
        var ending = random.Next(6) switch { 0 => "!", 1 => "?", 2 => "…", _ => "." };
        return $"{Openers[random.Next(Openers.Length)]} {string.Join(' ', words)}{ending}";
    }

    private static string Word(Random random) => Words[random.Next(Words.Length)];
}
