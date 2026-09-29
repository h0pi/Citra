using Citra.Application.Common.Embeddings;
using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pgvector;

namespace Citra.IntegrationTests.Embeddings;

/// <summary>
/// C-138: similar texts get similar vectors, and PostgreSQL (pgvector, cosine distance "&lt;=&gt;", the operator the
/// search will use) returns the closest chunks first. One test always runs with hand-made vectors; the real one
/// embeds with Voyage (opt-in, CITRA_REAL_API_TESTS=1).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SimilaritySearchTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const int Dimensions = Chunk.EmbeddingDimensions;

    [Fact]
    public async Task The_database_returns_the_nearest_chunks_first()
    {
        // Hand-made directions: "vacation" points along axis 0, "forklift" along axis 1.
        var vacation = Direction((0, 1.0f));
        var holidays = Direction((0, 0.9f), (2, 0.3f));      // close to "vacation"
        var forklift = Direction((1, 1.0f));                 // unrelated
        var question = Direction((0, 0.8f), (2, 0.2f), (1, 0.1f));
        var seed = await SeedChunksAsync(("vacation", vacation), ("holidays", holidays), ("forklift", forklift));

        var ranked = await NearestAsync(seed.Bot.Id, question);

        ranked.Select(r => r.Content).ShouldBe(["holidays", "vacation", "forklift"]);
        ranked.ShouldBe(ranked.OrderBy(r => r.Distance).ToList(), "sorted by distance");
        ranked[0].Distance.ShouldBe(1 - Cosine(holidays, question), tolerance: 1e-6, "pgvector's <=> is 1 - cosine similarity");
    }

    [Fact]
    public async Task Search_only_looks_at_the_given_bots_chunks()
    {
        var question = Direction((0, 1.0f));
        var mine = await SeedChunksAsync(("mine", Direction((1, 1.0f))));
        var other = await SeedChunksAsync(("perfect match, other bot", question));

        var ranked = await NearestAsync(mine.Bot.Id, question);

        ranked.Select(r => r.Content).ShouldBe(["mine"]);
        other.Bot.Id.ShouldNotBe(mine.Bot.Id);
    }

    [Fact]
    public async Task Real_voyage_puts_similar_sentences_together_and_finds_them_by_meaning()
    {
        var apiKey = RealVoyage.ApiKey();
        Assert.SkipWhen(apiKey is null, RealVoyage.SkipReason);

        await using var provider = RealVoyage.Services(apiKey!);
        var embeddings = provider.GetRequiredService<IEmbeddingService>();

        const string vacation = "Employees receive 25 days of paid vacation every year.";
        const string holidays = "Staff are entitled to twenty-five days of annual leave.";
        const string forklift = "Charge the forklift battery overnight before each shift.";

        // Request 1: the three chunks (one batch). Request 2: a question sharing no keywords with the answers.
        var documents = await embeddings.EmbedDocumentsAsync([vacation, holidays, forklift], Ct);
        var question = await embeddings.EmbedQueryAsync("How much paid time off do I get each year?", Ct);

        // Two similar sentences are closer to each other than either is to the different one.
        var similar = Cosine(documents.Vectors[0], documents.Vectors[1]);
        similar.ShouldBeGreaterThan(Cosine(documents.Vectors[0], documents.Vectors[2]));
        similar.ShouldBeGreaterThan(Cosine(documents.Vectors[1], documents.Vectors[2]));

        // Stored in PostgreSQL and searched with pgvector: both vacation chunks rank above the forklift one.
        var seed = await SeedChunksAsync(
            (vacation, documents.Vectors[0]), (holidays, documents.Vectors[1]), (forklift, documents.Vectors[2]));
        var ranked = await NearestAsync(seed.Bot.Id, question.Vector);

        ranked.Take(2).Select(r => r.Content).ShouldBe([vacation, holidays], ignoreOrder: true);
        ranked[^1].Content.ShouldBe(forklift);
        documents.TotalTokens.ShouldBeGreaterThan(0);
        question.TotalTokens.ShouldBeGreaterThan(0);
    }

    // ---------- helpers ----------

    private sealed record Ranked(string Content, double Distance);

    private sealed record Seed(Tenant Tenant, Bot Bot);

    /// <summary>Nearest chunks of one bot by cosine distance, computed by PostgreSQL itself.</summary>
    private async Task<List<Ranked>> NearestAsync(Guid botId, float[] question)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        var vector = new Vector(question);
        return await scope.Db.Database
            .SqlQuery<Ranked>($"""
                SELECT content AS "Content", embedding <=> {vector} AS "Distance"
                FROM chunks
                WHERE bot_id = {botId} AND embedding IS NOT NULL
                ORDER BY embedding <=> {vector}
                LIMIT 10
                """)
            .ToListAsync(Ct);
    }

    private async Task<Seed> SeedChunksAsync(params (string Content, float[] Embedding)[] chunks)
    {
        var tenant = Tenant.Create("Search company");
        tenant.ChangeSlug($"search-{Guid.NewGuid():N}");
        var bot = Bot.Create(tenant.Id, "Support");
        var document = Document.FromUpload(
            tenant.Id, bot.Id, "Handbook.pdf", "application/pdf", 100, DocumentSourceType.Pdf,
            $"{tenant.Id}/{bot.Id}/{Guid.NewGuid():N}/original.pdf", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));

        var entities = new List<object> { tenant, bot, document };
        for (var i = 0; i < chunks.Length; i++)
        {
            var chunk = Chunk.Create(document.Id, bot.Id, i, chunks[i].Content, null, 1, 0, chunks[i].Content.Length, 10);
            chunk.SetEmbedding(chunks[i].Embedding);
            entities.Add(chunk);
        }

        await using var system = fixture.OpenScope(tenantId: null);
        system.Db.AddRange(entities);
        await system.Db.SaveChangesAsync(Ct);
        return new Seed(tenant, bot);
    }

    /// <summary>A 1024-number vector with the given values on a few axes, scaled to length 1.</summary>
    private static float[] Direction(params (int Axis, float Value)[] parts)
    {
        var vector = new float[Dimensions];
        foreach (var (axis, value) in parts)
        {
            vector[axis] = value;
        }

        var length = MathF.Sqrt(vector.Sum(v => v * v));
        return vector.Select(v => v / length).ToArray();
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
