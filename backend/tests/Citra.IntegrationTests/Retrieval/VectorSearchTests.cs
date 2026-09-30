using Citra.Application.Common.Retrieval;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Citra.IntegrationTests.Retrieval.SearchData;

namespace Citra.IntegrationTests.Retrieval;

/// <summary>C-164: vector search in PostgreSQL — nearest in meaning first, one bot only, fast index or not.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class VectorSearchTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Returns_the_nearest_chunks_first_scored_by_cosine_similarity()
    {
        // Hand-made directions: "vacation" points along axis 0, "forklift" along axis 1.
        var vacation = Direction((0, 1.0f));
        var holidays = Direction((0, 0.9f), (2, 0.3f));
        var forklift = Direction((1, 1.0f));
        var question = Direction((0, 0.8f), (2, 0.2f), (1, 0.1f));
        var seed = await SeedAsync(fixture, [new("vacation", vacation), new("forklift", forklift), new("holidays", holidays)]);

        var found = await NearestAsync(seed.Bot.Id, question, limit: 10);

        found.Select(c => c.Content).ShouldBe(["holidays", "vacation", "forklift"]);
        found[0].Similarity!.Value.ShouldBe(Cosine(holidays, question), tolerance: 1e-6);
        found[1].Similarity!.Value.ShouldBe(Cosine(vacation, question), tolerance: 1e-6);
        found[2].Similarity!.Value.ShouldBe(Cosine(forklift, question), tolerance: 1e-6);
    }

    [Fact]
    public async Task Returns_what_a_citation_needs()
    {
        var question = Direction((0, 1.0f));
        var seed = await SeedAsync(
            fixture,
            [
                new("Intro", Direction((1, 1.0f))),
                new("Employees get 25 days of paid vacation.", question, PageNumber: 4, Heading: "Benefits › Vacation", CharStart: 120),
            ],
            documentName: "Employee handbook.pdf");

        var best = (await NearestAsync(seed.Bot.Id, question, limit: 1)).ShouldHaveSingleItem();

        var chunk = seed.Chunks[1];
        best.ShouldBe(new ChunkHit(
            chunk.Id, seed.Document.Id, "Employee handbook.pdf", 1, 4, "Benefits › Vacation",
            "Employees get 25 days of paid vacation.", 120, 120 + chunk.Content.Length, best.Similarity, KeywordScore: null));
        best.Similarity!.Value.ShouldBe(1, tolerance: 1e-6);
    }

    [Fact]
    public async Task Searches_only_the_given_bots_chunks()
    {
        var question = Direction((0, 1.0f));
        var mine = await SeedAsync(fixture, [new("mine", Direction((1, 1.0f)))]);
        await SeedAsync(fixture, [new("perfect match, other bot", question)]);

        var found = await NearestAsync(mine.Bot.Id, question, limit: 10);

        found.Select(c => c.Content).ShouldBe(["mine"]);
    }

    [Fact]
    public async Task Returns_at_most_the_limit_and_skips_chunks_without_embeddings()
    {
        var question = Direction((0, 1.0f));
        var seed = await SeedAsync(
            fixture,
            [
                new("not embedded yet", null),
                new("close", Direction((0, 1.0f), (1, 0.1f))),
                new("closer", Direction((0, 1.0f), (1, 0.05f))),
                new("far", Direction((1, 1.0f))),
            ]);

        (await NearestAsync(seed.Bot.Id, question, limit: 2)).Select(c => c.Content).ShouldBe(["closer", "close"]);
        (await NearestAsync(seed.Bot.Id, question, limit: 10)).Select(c => c.Content).ShouldBe(["closer", "close", "far"]);
    }

    [Fact]
    public async Task Looks_up_the_similarity_of_given_chunks_of_the_bot_only()
    {
        var question = Direction((0, 1.0f));
        var close = Direction((0, 0.8f), (1, 0.6f));
        var mine = await SeedAsync(fixture, [new("close", close), new("not embedded", null)]);
        var other = await SeedAsync(fixture, [new("other bot", question)]);
        var ids = new[] { mine.Chunks[0].Id, mine.Chunks[1].Id, other.Chunks[0].Id, Guid.NewGuid() };

        await using var scope = fixture.OpenScope(tenantId: null);
        var search = scope.Services.GetRequiredService<IChunkSearch>();
        var similarities = await search.SimilaritiesAsync(mine.Bot.Id, ids, question, Ct);

        similarities.Keys.ShouldBe([mine.Chunks[0].Id]);
        similarities[mine.Chunks[0].Id].ShouldBe(Cosine(close, question), tolerance: 1e-6);
        (await search.SimilaritiesAsync(mine.Bot.Id, [], question, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Leaves_no_setting_behind_on_the_connection()
    {
        var question = Direction((0, 1.0f));
        var seed = await SeedAsync(fixture, [new("only", question)]);

        await using var scope = fixture.OpenScope(tenantId: null);
        await scope.Db.Database.OpenConnectionAsync(Ct);
        try
        {
            await scope.Services.GetRequiredService<IChunkSearch>().NearestAsync(seed.Bot.Id, question, 5, Ct);

            // Pooled connections are reused by other requests: the search's settings must end with its transaction.
            var settings = await scope.Db.Database
                .SqlQueryRaw<string>("SELECT current_setting('hnsw.iterative_scan') || ' ' || current_setting('hnsw.ef_search') AS \"Value\"")
                .ToListAsync(Ct);
            settings.ShouldBe(["off 40"]);
            scope.Db.Database.CurrentTransaction.ShouldBeNull();
        }
        finally
        {
            await scope.Db.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task Runs_inside_the_callers_transaction_without_ending_it()
    {
        var question = Direction((0, 1.0f));
        var seed = await SeedAsync(fixture, [new("only", question)]);

        await using var scope = fixture.OpenScope(tenantId: null);
        await using var transaction = await scope.Db.Database.BeginTransactionAsync(Ct);

        var found = await scope.Services.GetRequiredService<IChunkSearch>().NearestAsync(seed.Bot.Id, question, 5, Ct);

        found.ShouldHaveSingleItem().Content.ShouldBe("only");
        scope.Db.Database.CurrentTransaction.ShouldBeSameAs(transaction);
        await transaction.CommitAsync(Ct);
    }

    [Theory]
    [InlineData(1023, 5)]
    [InlineData(1024, 0)]
    public async Task Rejects_a_wrong_sized_vector_or_limit(int dimensions, int limit)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        var search = scope.Services.GetRequiredService<IChunkSearch>();

        await Should.ThrowAsync<ArgumentException>(() => search.NearestAsync(Guid.NewGuid(), new float[dimensions], limit, Ct));
    }

    private async Task<IReadOnlyList<ChunkHit>> NearestAsync(Guid botId, float[] question, int limit)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        return await scope.Services.GetRequiredService<IChunkSearch>().NearestAsync(botId, question, limit, Ct);
    }
}
