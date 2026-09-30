using Citra.Application.Common.Retrieval;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using static Citra.IntegrationTests.Retrieval.SearchData;

namespace Citra.IntegrationTests.Retrieval;

/// <summary>
/// C-163/C-166: the retriever embeds the question (the fake embedding service gives each text its own fixed vector,
/// unrelated to any other) and combines the chunks nearest to it with the chunks sharing its words.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RetrieverTests(DatabaseFixture fixture)
{
    private const string Question = "How many vacation days do I get?";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Finds_the_chunk_matching_the_question_and_reports_the_question_tokens()
    {
        var seed = await SeedAsync(
            fixture,
            [
                new("Charge the forklift overnight.", FakeEmbeddingService.VectorFor("forklift")),
                new("Employees get 25 days of paid vacation.", FakeEmbeddingService.VectorFor(Question)),
                new("Parking is free.", FakeEmbeddingService.VectorFor("parking")),
            ]);

        var result = await SearchAsync(seed.Bot.Id, $"  {Question}\n", topK: 5);

        // Surrounding spaces are not part of the question: the trimmed text is embedded (the same vector).
        result.Chunks.Count.ShouldBe(3);
        result.Chunks[0].Content.ShouldBe("Employees get 25 days of paid vacation.");
        result.Chunks[0].Similarity!.Value.ShouldBe(1, tolerance: 1e-6);
        result.Chunks.Select(c => c.Score).ShouldBeInOrder(SortDirection.Descending);
        result.QueryTokens.ShouldBe(FakeEmbeddingService.TokensFor(Question));
    }

    [Fact]
    public async Task Finds_an_exact_code_by_its_words_although_its_meaning_is_not_close()
    {
        // Every vector here is unrelated to the question's, so vector search alone ranks them at random.
        var seed = await SeedAsync(
            fixture,
            Enumerable.Range(1, 8)
                .Select(i => new SeedChunk($"Card error number {i}.", FakeEmbeddingService.VectorFor($"card {i}")))
                .Append(new SeedChunk("Error ERR-4012 means the card was declined.", FakeEmbeddingService.VectorFor("declined"))));

        var result = await SearchAsync(seed.Bot.Id, "What does ERR-4012 mean?", topK: 3);

        var best = result.Chunks[0];
        best.Content.ShouldBe("Error ERR-4012 means the card was declined.");
        best.KeywordRank.ShouldBe(1);
        best.VectorRank.ShouldNotBeNull(); // few chunks: vector search returned all of them
        best.Score.ShouldBe((1.0 / (ReciprocalRankFusion.K + 1)) + (1.0 / (ReciprocalRankFusion.K + best.VectorRank!.Value)), tolerance: 1e-12);
    }

    [Fact]
    public async Task Looks_further_than_top_k_in_each_search_before_combining()
    {
        // Vector search: "Parking" 1st (same vector as the question), "Refund policy" 2nd (similar vector).
        // Keyword search: only "Refund policy". Combined, "Refund policy" is best: 1/62 + 1/61 > 1/61. Asking each
        // search for only one result would miss its 2nd vector place and put "Parking" first.
        const string question = "refund policy";
        var seed = await SeedAsync(
            fixture,
            [
                new("Parking is free for employees.", FakeEmbeddingService.VectorFor(question)),
                new("Our refund policy explained.", Blend(FakeEmbeddingService.VectorFor(question), FakeEmbeddingService.VectorFor("other"))),
                new("Holiday schedule.", FakeEmbeddingService.VectorFor("holidays")),
            ]);

        var result = await SearchAsync(seed.Bot.Id, question, topK: 1);

        var best = result.Chunks.ShouldHaveSingleItem();
        best.Content.ShouldBe("Our refund policy explained.");
        (best.VectorRank, best.KeywordRank).ShouldBe((2, 1));
    }

    [Fact]
    public async Task Drops_keyword_matches_that_cannot_be_judged_because_they_have_no_embedding()
    {
        var seed = await SeedAsync(fixture, [new("Refunds take 14 days.", null)]);

        var result = await SearchAsync(seed.Bot.Id, "refund", topK: 5);

        result.Chunks.ShouldBeEmpty();
    }

    [Fact]
    public async Task Returns_overlapping_neighbours_as_one_passage()
    {
        const string page = "Employees get 25 days of paid vacation. Unused days carry over until March.";
        var seed = await SeedAsync(
            fixture,
            [
                new(page[..39], FakeEmbeddingService.VectorFor(Question), CharStart: 0),
                new(page[24..], FakeEmbeddingService.VectorFor("carry over"), CharStart: 24),
                new("Parking is free.", FakeEmbeddingService.VectorFor("parking"), PageNumber: 2),
            ]);

        var result = await SearchAsync(seed.Bot.Id, Question, topK: 5);

        result.Chunks.Select(c => (c.Content, c.ChunkCount)).ShouldBe([(page, 2), ("Parking is free.", 1)]);
        result.Chunks[0].ChunkId.ShouldBe(seed.Chunks[0].Id);
    }

    [Fact]
    public async Task Returns_at_most_top_k_chunks()
    {
        var seed = await SeedAsync(
            fixture, Enumerable.Range(1, 6).Select(i => new SeedChunk($"Chunk {i}", FakeEmbeddingService.VectorFor($"chunk {i}"))));

        var result = await SearchAsync(seed.Bot.Id, Question, topK: 4);

        result.Chunks.Count.ShouldBe(4);
    }

    [Fact]
    public async Task A_bot_without_chunks_finds_nothing()
    {
        var seed = await SeedAsync(fixture, []);

        var result = await SearchAsync(seed.Bot.Id, Question, topK: 5);

        result.Chunks.ShouldBeEmpty();
        result.QueryTokens.ShouldBe(FakeEmbeddingService.TokensFor(Question));
    }

    [Theory]
    [InlineData(false, Question, 5)]
    [InlineData(true, "", 5)]
    [InlineData(true, "   ", 5)]
    [InlineData(true, Question, 0)]
    [InlineData(true, Question, IRetriever.MaxTopK + 1)]
    public async Task Rejects_invalid_searches(bool withBot, string query, int topK)
    {
        await Should.ThrowAsync<ArgumentException>(() => SearchAsync(withBot ? Guid.NewGuid() : Guid.Empty, query, topK));
    }

    [Fact]
    public async Task Accepts_the_largest_top_k()
    {
        var seed = await SeedAsync(fixture, [new("Only chunk", FakeEmbeddingService.VectorFor("only"))]);

        var result = await SearchAsync(seed.Bot.Id, Question, IRetriever.MaxTopK);

        result.Chunks.ShouldHaveSingleItem();
    }

    [Fact]
    public void Each_search_looks_at_three_times_top_k_but_at_least_30()
    {
        Retriever.CandidatesFor(1).ShouldBe(30);
        Retriever.CandidatesFor(10).ShouldBe(30);
        Retriever.CandidatesFor(11).ShouldBe(33);
        Retriever.CandidatesFor(IRetriever.MaxTopK).ShouldBe(150);
    }

    /// <summary>Halfway between two directions: similar to both (cosine about 0.7 for unrelated unit vectors).</summary>
    private static float[] Blend(float[] a, float[] b)
    {
        var sum = a.Zip(b, (x, y) => x + y).ToArray();
        var length = MathF.Sqrt(sum.Sum(v => v * v));
        return [.. sum.Select(v => v / length)];
    }

    private async Task<RetrievalResult> SearchAsync(Guid botId, string query, int topK)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        return await scope.Services.GetRequiredService<IRetriever>().SearchAsync(botId, query, topK, Ct);
    }
}
