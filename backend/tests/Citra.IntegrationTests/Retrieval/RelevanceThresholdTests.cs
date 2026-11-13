using Citra.Application.Common.Retrieval;
using Citra.Infrastructure;
using Citra.Infrastructure.Options;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static Citra.IntegrationTests.Retrieval.SearchData;

namespace Citra.IntegrationTests.Retrieval;

/// <summary>
/// Chunks not similar enough in meaning to the question are dropped, however they were found; chunks that
/// contain the question's words need less similarity.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RelevanceThresholdTests(DatabaseFixture fixture)
{
    private const string Question = "How many vacation days do I get?";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static float[] QuestionVector => FakeEmbeddingService.VectorFor(Question);

    [Fact]
    public async Task Drops_chunks_less_similar_than_the_minimum()
    {
        var seed = await SeedAsync(
            fixture,
            [
                new("unrelated", FakeEmbeddingService.VectorFor("unrelated")),        // about 0
                new("close", Toward(QuestionVector, 1.0f, "close")),                  // about 0.71
                new("same", QuestionVector),                                          // 1
                new("somewhat", Toward(QuestionVector, 0.4f, "somewhat")),            // about 0.37
            ]);

        var result = await SearchAsync(seed.Bot.Id, minSimilarity: 0.5);

        result.Chunks.Select(c => c.Content).ShouldBe(["same", "close"]);
        result.Chunks.ShouldAllBe(c => c.Similarity >= 0.5);
    }

    [Fact]
    public async Task A_chunk_exactly_at_the_minimum_is_kept()
    {
        var seed = await SeedAsync(fixture, [new("same", QuestionVector)]);
        var similarity = (await SearchAsync(seed.Bot.Id, minSimilarity: -1)).Chunks.ShouldHaveSingleItem().Similarity!.Value;

        var result = await SearchAsync(seed.Bot.Id, minSimilarity: similarity);

        result.Chunks.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Judges_keyword_matches_beyond_the_vector_results_by_their_meaning_too()
    {
        // 31 chunks closer in meaning fill the vector search's 30 places; the two "vacation" chunks are found by
        // their words only (the related one has more of them: keyword place 1). The one with a related meaning
        // stays (its similarity is looked up), the unrelated one goes.
        // Exact search: the fast index is approximate and might return a filler less, letting a "vacation" chunk in.
        var closer = Enumerable.Range(1, 31).Select(i => new SeedChunk($"Filler {i}", Toward(QuestionVector, 1.0f, $"filler {i}")));
        var seed = await SeedAsync(
            fixture,
            closer
                .Append(new SeedChunk("Vacation days: related meaning.", Toward(QuestionVector, 0.66f, "related")))  // about 0.55
                .Append(new SeedChunk("Vacation: unrelated meaning.", FakeEmbeddingService.VectorFor("unrelated"))));

        var result = await SearchAsync(seed.Bot.Id, minSimilarity: 0.5, topK: 5, exact: true);

        var related = result.Chunks.Where(c => c.Content.StartsWith("Vacation")).ShouldHaveSingleItem();
        related.Content.ShouldBe("Vacation days: related meaning.");
        (related.VectorRank, related.KeywordRank).ShouldBe(((int?)null, 1));
        related.Similarity!.Value.ShouldBe(Cosine(seed.Chunks[31].Embedding!, QuestionVector), tolerance: 1e-6);
        result.Chunks.Count.ShouldBe(5);
    }

    [Fact]
    public async Task A_chunk_containing_the_questions_words_needs_less_similarity()
    {
        var seed = await SeedAsync(
            fixture,
            [
                new("Vacation days explained.", Toward(QuestionVector, 0.4f, "words")),     // about 0.37, words match
                new("Somewhat related text.", Toward(QuestionVector, 0.4f, "no words")),    // about 0.37, no words
                new("Vacation trivia.", FakeEmbeddingService.VectorFor("trivia")),          // about 0, words match
            ]);

        var result = await SearchAsync(seed.Bot.Id, minSimilarity: 0.5, minKeywordSimilarity: 0.3);

        var kept = result.Chunks.ShouldHaveSingleItem();
        kept.Content.ShouldBe("Vacation days explained.");
        kept.KeywordRank.ShouldNotBeNull();
    }

    [Fact]
    public async Task Weak_matches_do_not_take_places_from_good_ones()
    {
        // The two unrelated chunks rank first by their words; top 2 must still be the two related ones.
        var seed = await SeedAsync(
            fixture,
            [
                new("Vacation vacation days.", FakeEmbeddingService.VectorFor("a")),
                new("Vacation days vacation.", FakeEmbeddingService.VectorFor("b")),
                new("Paid leave rules.", Toward(QuestionVector, 1.0f, "leave")),
                new("Time off policy.", Toward(QuestionVector, 0.9f, "time off")),
            ]);

        var result = await SearchAsync(seed.Bot.Id, minSimilarity: 0.5, topK: 2);

        result.Chunks.Select(c => c.Content).ShouldBe(["Paid leave rules.", "Time off policy."]);
    }

    [Fact]
    public async Task The_minimums_come_from_settings_and_default_to_0_25_and_0_15()
    {
        var defaults = await Settings([]);
        (defaults.MinSimilarity, defaults.MinKeywordSimilarity).ShouldBe((0.25, 0.15));
        var configured = await Settings([("Retrieval:MinSimilarity", "0.4"), ("Retrieval:MinKeywordSimilarity", "0.3")]);
        (configured.MinSimilarity, configured.MinKeywordSimilarity).ShouldBe((0.4, 0.3));
        var off = await Settings([("Retrieval:MinSimilarity", "-1"), ("Retrieval:MinKeywordSimilarity", "-1")]);
        (off.MinSimilarity, off.MinKeywordSimilarity).ShouldBe((-1, -1));
    }

    [Theory]
    [InlineData("Retrieval:MinSimilarity", "1.01")]
    [InlineData("Retrieval:MinSimilarity", "-1.5")]
    [InlineData("Retrieval:MinKeywordSimilarity", "1.01")]
    [InlineData("Retrieval:MinKeywordSimilarity", "-1.5")]
    public async Task A_minimum_outside_minus_1_to_1_is_rejected_at_startup(string key, string value)
    {
        await Should.ThrowAsync<OptionsValidationException>(() => Settings([(key, value)]));
    }

    [Fact]
    public void The_api_uses_the_measured_minimums()
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(ApiAppSettings()).Build();

        configuration.GetValue<double>("Retrieval:MinSimilarity").ShouldBe(0.25);
        configuration.GetValue<double>("Retrieval:MinKeywordSimilarity").ShouldBe(0.15);
    }

    /// <summary>
    /// <paramref name="weight"/> parts of <paramref name="target"/> plus one part of an unrelated direction: cosine
    /// to the target is about weight / sqrt(weight² + 1).
    /// </summary>
    private static float[] Toward(float[] target, float weight, string noise)
    {
        var other = FakeEmbeddingService.VectorFor(noise);
        var sum = target.Zip(other, (t, o) => (weight * t) + o).ToArray();
        var length = MathF.Sqrt(sum.Sum(v => v * v));
        return [.. sum.Select(v => v / length)];
    }

    /// <summary>Searches with the given minimums (the keyword one defaults to the same as the other).</summary>
    private async Task<RetrievalResult> SearchAsync(
        Guid botId, double minSimilarity, int topK = 10, bool exact = false, double? minKeywordSimilarity = null)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        await using var transaction = await scope.Db.Database.BeginTransactionAsync(Ct);
        if (exact)
        {
            // Without index scans the planner reads the bot's rows and sorts them all by distance.
            await scope.Db.Database.ExecuteSqlRawAsync("SET LOCAL enable_indexscan = off", Ct);
        }

        var retriever = new Retriever(
            fixture.Embeddings, scope.Services.GetRequiredService<IChunkSearch>(), new FixedSettings(minSimilarity, minKeywordSimilarity ?? minSimilarity));
        return await retriever.SearchAsync(botId, Question, topK, Ct);
    }

    private static async Task<IRetrievalSettings> Settings((string Key, string Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values
                .Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))
                .Append(new("ConnectionStrings:Database", "Host=unused;Database=unused")))
            .Build();
        var services = new ServiceCollection().AddLogging();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IRetrievalSettings>();
    }

    private static string ApiAppSettings()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory!.FullName, "src", "Citra.Api", "appsettings.json")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory.FullName, "src", "Citra.Api", "appsettings.json");
    }

    private sealed record FixedSettings(double MinSimilarity, double MinKeywordSimilarity) : IRetrievalSettings;
}
