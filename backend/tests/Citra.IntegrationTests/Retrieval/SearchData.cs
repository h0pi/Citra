using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.IntegrationTests.Infrastructure;

namespace Citra.IntegrationTests.Retrieval;

/// <summary>
/// A chunk to seed for a search test. A null embedding = a chunk not embedded yet. Without a
/// <paramref name="CharStart"/>, chunks follow each other on the page without sharing text (so they are never
/// joined as overlapping neighbours).
/// </summary>
internal sealed record SeedChunk(
    string Content, float[]? Embedding, int? PageNumber = 1, string? Heading = null, int? CharStart = null);

/// <summary>A company with one bot and one document holding the seeded chunks.</summary>
internal sealed record SearchSeed(Tenant Tenant, Bot Bot, Document Document, IReadOnlyList<Chunk> Chunks);

/// <summary>Seeds bots with chunks and builds hand-made vectors whose distances are known in advance.</summary>
internal static class SearchData
{
    public static async Task<SearchSeed> SeedAsync(
        DatabaseFixture fixture, IEnumerable<SeedChunk> chunks, string documentName = "Handbook.pdf")
    {
        var tenant = Tenant.Create("Search company");
        tenant.ChangeSlug($"search-{Guid.NewGuid():N}");
        var bot = Bot.Create(tenant.Id, "Support");
        var document = Document.FromUpload(
            tenant.Id, bot.Id, documentName, "application/pdf", 100, DocumentSourceType.Pdf,
            $"{tenant.Id}/{bot.Id}/{Guid.NewGuid():N}/original.pdf", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));

        var next = 0;
        var created = chunks
            .Select((c, i) =>
            {
                var start = c.CharStart ?? next;
                next = start + c.Content.Length + 1;
                var chunk = Chunk.Create(
                    document.Id, bot.Id, i, c.Content, c.Heading, c.PageNumber, start, start + c.Content.Length, 10);
                if (c.Embedding is not null)
                {
                    chunk.SetEmbedding(c.Embedding);
                }

                return chunk;
            })
            .ToList();

        await using var system = fixture.OpenScope(tenantId: null);
        system.Db.AddRange(tenant, bot, document);
        system.Db.AddRange(created);
        await system.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new SearchSeed(tenant, bot, document, created);
    }

    /// <summary>A 1024-number vector with the given values on a few axes, scaled to length 1.</summary>
    public static float[] Direction(params (int Axis, float Value)[] parts)
    {
        var vector = new float[Chunk.EmbeddingDimensions];
        foreach (var (axis, value) in parts)
        {
            vector[axis] = value;
        }

        var length = MathF.Sqrt(vector.Sum(v => v * v));
        return [.. vector.Select(v => v / length)];
    }

    public static double Cosine(float[] a, float[] b)
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
