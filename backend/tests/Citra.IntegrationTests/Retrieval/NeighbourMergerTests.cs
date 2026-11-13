using Citra.Application.Common.Retrieval;

namespace Citra.IntegrationTests.Retrieval;

/// <summary>Overlapping neighbour chunks of one page are joined into one passage.</summary>
public sealed class NeighbourMergerTests
{
    private const string Page =
        "Vacation. Employees get 25 days per year. Unused days carry over until March. Ask your manager first.";

    private static readonly Guid Handbook = Guid.NewGuid();

    [Fact]
    public void Joins_two_overlapping_neighbours_into_the_pages_exact_text()
    {
        // Chunk 1 repeats "Unused days carry over until March." — the end of chunk 0.
        var first = Cut(index: 0, start: 0, end: 78, score: 0.02);
        var second = Cut(index: 1, start: 42, end: Page.Length, score: 0.03);

        var passage = NeighbourMerger.Merge([second, first]).ShouldHaveSingleItem();

        passage.Content.ShouldBe(Page);
        (passage.CharStart, passage.CharEnd).ShouldBe((0, Page.Length));
        (passage.ChunkId, passage.Index, passage.ChunkCount).ShouldBe((first.ChunkId, 0, 2));
        passage.Score.ShouldBe(0.03);
    }

    [Fact]
    public void Joins_a_whole_run_of_neighbours()
    {
        var passage = NeighbourMerger.Merge(
            [Cut(index: 2, start: 60, end: Page.Length), Cut(index: 0, start: 0, end: 40), Cut(index: 1, start: 30, end: 70)])
            .ShouldHaveSingleItem();

        passage.Content.ShouldBe(Page);
        passage.ChunkCount.ShouldBe(3);
    }

    [Fact]
    public void A_joined_passage_takes_the_place_of_its_best_part()
    {
        var other = Cut(index: 0, start: 0, end: 20, document: Guid.NewGuid());
        var first = Cut(index: 0, start: 0, end: 50);
        var second = Cut(index: 1, start: 40, end: Page.Length);

        NeighbourMerger.Merge([first, other, second]).Select(c => c.ChunkCount).ShouldBe([2, 1]);
        var merged = NeighbourMerger.Merge([other, second, first]);
        merged.Select(c => c.ChunkCount).ShouldBe([1, 2]);
        merged[0].ShouldBe(other);
    }

    [Fact]
    public void Keeps_each_searchs_best_score_and_place()
    {
        var first = Cut(index: 0, start: 0, end: 50) with
        {
            Score = 0.02, Similarity = 0.5, KeywordScore = null, VectorRank = 5, KeywordRank = null,
        };
        var second = Cut(index: 1, start: 40, end: Page.Length) with
        {
            Score = 0.03, Similarity = 0.6, KeywordScore = 0.4, VectorRank = 3, KeywordRank = 1,
        };

        var passage = NeighbourMerger.Merge([first, second]).ShouldHaveSingleItem();

        (passage.Score, passage.Similarity, passage.KeywordScore).ShouldBe((0.03, (double?)0.6, (double?)0.4));
        (passage.VectorRank, passage.KeywordRank).ShouldBe(((int?)3, (int?)1));
    }

    [Fact]
    public void A_neighbour_inside_the_previous_one_adds_nothing()
    {
        var passage = NeighbourMerger.Merge([Cut(index: 0, start: 0, end: 60), Cut(index: 1, start: 20, end: 50)])
            .ShouldHaveSingleItem();

        (passage.Content, passage.CharEnd, passage.ChunkCount).ShouldBe((Page[..60], 60, 2));
    }

    [Theory]
    [InlineData("touching")]       // next starts exactly where the previous ends: no shared text (new section)
    [InlineData("gap in index")]   // chunks 0 and 2: the text in between was not found
    [InlineData("other page")]
    [InlineData("other document")]
    [InlineData("bad offsets")]    // content does not match its offsets: joining could garble the text
    [InlineData("starts earlier")] // next starts before the previous: not how the chunker cuts
    public void Leaves_chunks_that_do_not_overlap_as_they_are(string case_)
    {
        var first = Cut(index: 0, start: 0, end: 50);
        var second = case_ switch
        {
            "touching" => Cut(index: 1, start: 50, end: Page.Length),
            "gap in index" => Cut(index: 2, start: 40, end: Page.Length),
            "other page" => Cut(index: 1, start: 40, end: Page.Length) with { PageNumber = 2 },
            "other document" => Cut(index: 1, start: 40, end: Page.Length, document: Guid.NewGuid()),
            "bad offsets" => Cut(index: 1, start: 40, end: Page.Length) with { CharEnd = Page.Length + 5 },
            _ => Cut(index: 1, start: 40, end: Page.Length) with { CharStart = 0, Content = Page },
        };
        if (case_ == "starts earlier")
        {
            first = Cut(index: 0, start: 10, end: 50);
        }

        NeighbourMerger.Merge([first, second]).ShouldBe([first, second]);
    }

    [Fact]
    public void Joins_neighbours_of_sources_without_pages()
    {
        var first = Cut(index: 0, start: 0, end: 50) with { PageNumber = null };
        var second = Cut(index: 1, start: 40, end: Page.Length) with { PageNumber = null };

        NeighbourMerger.Merge([first, second]).ShouldHaveSingleItem().Content.ShouldBe(Page);
    }

    [Fact]
    public void Nothing_to_join_changes_nothing()
    {
        NeighbourMerger.Merge([]).ShouldBeEmpty();
        var one = Cut(index: 4, start: 10, end: 30);
        NeighbourMerger.Merge([one]).ShouldBe([one]);
    }

    /// <summary>A chunk holding exactly <c>Page[start..end]</c>, as the chunker cuts them.</summary>
    private static RetrievedChunk Cut(int index, int start, int end, double score = 0.01, Guid? document = null) =>
        new(Guid.NewGuid(), document ?? Handbook, "Handbook.pdf", index, 1, "Vacation", Page[start..end], start, end,
            score, Similarity: 0.5, KeywordScore: null, VectorRank: 1, KeywordRank: null);
}
