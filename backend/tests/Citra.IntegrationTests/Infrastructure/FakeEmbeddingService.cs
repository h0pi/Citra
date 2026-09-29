using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Citra.Application.Common.Embeddings;
using Citra.Domain.Documents;

namespace Citra.IntegrationTests.Infrastructure;

/// <summary>
/// Stands in for Voyage in database tests: no network, no cost. Every text gets its own fixed vector (the same
/// text always the same vector), tokens are counted as length/4 + 1, and every call is recorded. A test can run
/// code before each call (<see cref="Intercept"/>): wait, fail, or look at the database mid-processing.
/// </summary>
public sealed class FakeEmbeddingService : IEmbeddingService
{
    public const string ModelName = "fake-embedding";

    private readonly ConcurrentQueue<IReadOnlyList<string>> _documentCalls = new();
    private Func<IReadOnlyList<string>, CancellationToken, Task>? _beforeEmbed;

    public string Model => ModelName;

    public int Dimensions => Chunk.EmbeddingDimensions;

    public decimal PricePerMillionTokensUsd => 0.06m;

    /// <summary>The texts of every EmbedDocumentsAsync call, in call order.</summary>
    public IReadOnlyList<IReadOnlyList<string>> DocumentCalls => [.. _documentCalls];

    /// <summary>Runs <paramref name="beforeEmbed"/> before every call until the returned handle is disposed.</summary>
    public IDisposable Intercept(Func<IReadOnlyList<string>, CancellationToken, Task> beforeEmbed)
    {
        _beforeEmbed = beforeEmbed;
        return new Restore(this);
    }

    public async Task<DocumentEmbeddings> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        _documentCalls.Enqueue([.. texts]);
        if (_beforeEmbed is { } hook)
        {
            await hook(texts, cancellationToken);
        }

        return new DocumentEmbeddings([.. texts.Select(VectorFor)], texts.Sum(TokensFor));
    }

    public Task<QueryEmbedding> EmbedQueryAsync(string text, CancellationToken cancellationToken) =>
        Task.FromResult(new QueryEmbedding(VectorFor(text), TokensFor(text)));

    public static int TokensFor(string text) => (text.Length / 4) + 1;

    /// <summary>A fixed unit-length vector derived from the text's SHA-256.</summary>
    public static float[] VectorFor(string text)
    {
        var seed = BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        var random = new Random(seed);
        var vector = new float[Chunk.EmbeddingDimensions];
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(random.NextDouble() - 0.5);
        }

        var length = MathF.Sqrt(vector.Sum(v => v * v));
        return [.. vector.Select(v => v / length)];
    }

    private sealed class Restore(FakeEmbeddingService service) : IDisposable
    {
        public void Dispose() => service._beforeEmbed = null;
    }
}
