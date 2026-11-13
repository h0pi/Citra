using Citra.Application.Common.Retrieval;

namespace Citra.IntegrationTests.Retrieval;

/// <summary>Combining the vector and keyword rankings (Reciprocal Rank Fusion, k = 60).</summary>
public sealed class ReciprocalRankFusionTests
{
    private static readonly ChunkHit A = Hit("A"), B = Hit("B"), C = Hit("C"), D = Hit("D");

    [Fact]
    public void A_chunk_both_searches_found_beats_chunks_only_one_search_found()
    {
        var fused = ReciprocalRankFusion.Fuse(
            vector: [A with { Similarity = 0.9 }, B with { Similarity = 0.8 }, C with { Similarity = 0.7 }],
            keyword: [B with { KeywordScore = 0.5 }, D with { KeywordScore = 0.2 }]);

        // B: 1/62 + 1/61; A: 1/61; D: 1/62; C: 1/63.
        fused.Select(c => c.Content).ShouldBe(["B", "A", "D", "C"]);
        fused[0].Score.ShouldBe((1.0 / 62) + (1.0 / 61), tolerance: 1e-12);
        fused[1].Score.ShouldBe(1.0 / 61, tolerance: 1e-12);
        fused[2].Score.ShouldBe(1.0 / 62, tolerance: 1e-12);
        fused[3].Score.ShouldBe(1.0 / 63, tolerance: 1e-12);
    }

    [Fact]
    public void Keeps_each_searchs_score_and_place()
    {
        var fused = ReciprocalRankFusion.Fuse(
            vector: [A with { Similarity = 0.9 }, B with { Similarity = 0.8 }],
            keyword: [B with { KeywordScore = 0.5 }, D with { KeywordScore = 0.2 }]);

        var b = fused.Single(c => c.Content == "B");
        (b.Similarity, b.VectorRank, b.KeywordScore, b.KeywordRank).ShouldBe((0.8, 2, 0.5, 1));
        var a = fused.Single(c => c.Content == "A");
        (a.Similarity, a.VectorRank, a.KeywordScore, a.KeywordRank).ShouldBe((0.9, 1, (double?)null, (int?)null));
        var d = fused.Single(c => c.Content == "D");
        (d.Similarity, d.VectorRank, d.KeywordScore, d.KeywordRank).ShouldBe(((double?)null, (int?)null, 0.2, 2));
    }

    [Fact]
    public void Keeps_what_a_citation_needs()
    {
        var hit = new ChunkHit(Guid.NewGuid(), Guid.NewGuid(), "Handbook.pdf", 7, 3, "Benefits", "text", 10, 14, 0.5, null);

        var chunk = ReciprocalRankFusion.Fuse([hit], []).ShouldHaveSingleItem();

        chunk.ShouldBe(new RetrievedChunk(
            hit.ChunkId, hit.DocumentId, "Handbook.pdf", 7, 3, "Benefits", "text", 10, 14,
            1.0 / 61, Similarity: 0.5, KeywordScore: null, VectorRank: 1, KeywordRank: null));
    }

    [Fact]
    public void Equal_scores_go_to_the_better_single_place_then_to_the_better_vector_place()
    {
        // A: vector 1st only; D: keyword 1st only (equal scores and best places: the vector result wins).
        // B: vector 2nd + keyword 3rd; C: vector 3rd + keyword 2nd (equal scores and best places: B's vector place wins).
        var fused = ReciprocalRankFusion.Fuse(vector: [A, B, C], keyword: [D, C, B]);

        fused.Select(c => c.Content).ShouldBe(["B", "C", "A", "D"]);
    }

    [Fact]
    public void A_first_place_beats_the_same_score_made_of_two_low_places()
    {
        // X: keyword 1st only = 1/61. Y: 62nd in both = 1/122 + 1/122 = 1/61 exactly. X's first place wins,
        // although only Y has a vector place.
        var fillers = Enumerable.Range(1, 61).Select(i => Hit($"filler {i}")).ToList();
        var x = Hit("X");
        var y = Hit("Y");

        var fused = ReciprocalRankFusion.Fuse(vector: [.. fillers, y], keyword: [x, .. fillers.Skip(1), y]);

        var xAt = fused.Select(c => c.Content).ToList().IndexOf("X");
        var yAt = fused.Select(c => c.Content).ToList().IndexOf("Y");
        fused[xAt].Score.ShouldBe(fused[yAt].Score);
        (fused[yAt].VectorRank, fused[yAt].KeywordRank).ShouldBe((62, 62));
        xAt.ShouldBe(yAt - 1);
    }

    [Fact]
    public void One_empty_list_keeps_the_others_order_and_two_give_nothing()
    {
        ReciprocalRankFusion.Fuse([], [C, A, B]).Select(c => c.Content).ShouldBe(["C", "A", "B"]);
        ReciprocalRankFusion.Fuse([B, C], []).Select(c => c.Content).ShouldBe(["B", "C"]);
        ReciprocalRankFusion.Fuse([], []).ShouldBeEmpty();
    }

    [Fact]
    public void A_chunk_listed_twice_in_one_search_counts_once_at_its_first_place()
    {
        var fused = ReciprocalRankFusion.Fuse([A, B, A], []);

        fused.Select(c => (c.Content, c.VectorRank)).ShouldBe([("A", (int?)1), ("B", 2)]);
        fused[0].Score.ShouldBe(1.0 / 61, tolerance: 1e-12);
    }

    private static ChunkHit Hit(string name) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Doc.pdf", 0, 1, null, name, 0, name.Length, null, null);
}
