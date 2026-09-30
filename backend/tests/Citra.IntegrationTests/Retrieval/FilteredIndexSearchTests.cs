using Citra.Application.Common.Retrieval;
using Citra.Domain.Documents;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pgvector;
using static Citra.IntegrationTests.Retrieval.SearchData;

namespace Citra.IntegrationTests.Retrieval;

/// <summary>
/// C-164: vector search through the fast (approximate) HNSW index, which finds the nearest chunks of all bots
/// before the bot filter is applied. Own database: the index's graph is built from every vector in the table.
/// </summary>
[Collection(SearchIndexCollection.Name)]
public sealed class FilteredIndexSearchTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Finds_a_bots_chunks_even_when_other_bots_have_many_closer_ones()
    {
        // 300 chunks of another company, all nearer to the question than any of this bot's three chunks. The HNSW
        // index keeps a list of the nearest candidates (100 in the search): without iterative scan, all of them
        // belong to the other bot and are filtered out, leaving this bot with nothing. (Random noise keeps the vectors
        // realistic: regular hand-made ones are all equally far apart, which leaves the index graph with too few links.)
        var question = Direction((0, 1.0f));
        var random = new Random(42);
        await SeedAsync(fixture, Enumerable.Range(1, 300).Select(i => new SeedChunk($"other {i}", Noisy(random, 1.0f, 0.03f))));
        var mine = await SeedAsync(
            fixture,
            [
                new("mine, nearest", Noisy(random, 0.8f, 0.03f)),
                new("mine, middle", Noisy(random, 0.75f, 0.03f)),
                new("mine, farthest", Noisy(random, 0.7f, 0.03f)),
            ]);

        await using var scope = fixture.OpenScope(tenantId: null);
        await using var transaction = await scope.Db.Database.BeginTransactionAsync(Ct);
        // Only the HNSW index is left for the planner: no bot_id index (dropped until the rollback below), no full
        // table scan, no sorting (the index returns rows already ordered by distance).
        await scope.Db.Database.ExecuteSqlRawAsync(
            "DROP INDEX ix_chunks_bot_id; SET LOCAL enable_seqscan = off; SET LOCAL enable_bitmapscan = off; SET LOCAL enable_sort = off", Ct);

        // The trap is real: the same search without iterative scan misses this bot's chunks.
        await scope.Db.Database.ExecuteSqlRawAsync("SET LOCAL hnsw.ef_search = 100", Ct);
        var vector = new Vector(question);
        var withoutIterativeScan = await scope.Db.Database
            .SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM chunks WHERE bot_id = {mine.Bot.Id} AND embedding IS NOT NULL
                ORDER BY embedding <=> {vector} LIMIT 3
                """)
            .ToListAsync(Ct);
        withoutIterativeScan.Count.ShouldBeLessThan(3);

        var found = await scope.Services.GetRequiredService<IChunkSearch>().NearestAsync(mine.Bot.Id, question, 3, Ct);

        found.Select(c => c.Content).ShouldBe(["mine, nearest", "mine, middle", "mine, farthest"]);
        await transaction.RollbackAsync(Ct);
    }

    /// <summary>A vector with <paramref name="axis0"/> on the question's axis and random noise on all others.</summary>
    private static float[] Noisy(Random random, float axis0, float noise)
    {
        var parts = new (int, float)[Chunk.EmbeddingDimensions];
        parts[0] = (0, axis0);
        for (var i = 1; i < parts.Length; i++)
        {
            parts[i] = (i, (float)((random.NextDouble() * 2) - 1) * noise);
        }

        return Direction(parts);
    }
}
