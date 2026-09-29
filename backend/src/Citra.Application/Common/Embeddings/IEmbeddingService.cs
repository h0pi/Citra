namespace Citra.Application.Common.Embeddings;

/// <summary>
/// Turns text into embedding vectors (lists of numbers whose distance reflects meaning), so a question can be
/// matched with the chunks that answer it. Documents and questions are embedded differently on purpose: models
/// like Voyage are trained to place a question near the passages that answer it, not near other questions.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>Embedding model name, e.g. "voyage-4" (stored with usage stats).</summary>
    string Model { get; }

    /// <summary>Numbers per vector; always equal to the database column size (Chunk.EmbeddingDimensions).</summary>
    int Dimensions { get; }

    /// <summary>Price of the model, for recording costs (see <see cref="EmbeddingCost"/> and EmbeddingUsage).</summary>
    decimal PricePerMillionTokensUsd { get; }

    /// <summary>
    /// Embeds chunk texts for storage. Returns exactly one vector per text, in the same order. An empty list
    /// returns an empty result without calling the provider. Texts must not be blank.
    /// </summary>
    Task<DocumentEmbeddings> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);

    /// <summary>Embeds a user's question for searching the stored chunks.</summary>
    Task<QueryEmbedding> EmbedQueryAsync(string text, CancellationToken cancellationToken);
}

/// <summary>Vectors for a list of texts (same order), and the tokens the provider billed for them.</summary>
public sealed record DocumentEmbeddings(IReadOnlyList<float[]> Vectors, int TotalTokens)
{
    public static DocumentEmbeddings Empty { get; } = new([], 0);

    /// <summary>
    /// Checks the promise every implementation makes: one vector per text, each of the expected size, with real
    /// numbers. A wrong count would attach vectors to the wrong chunks; a wrong size could not be stored.
    /// </summary>
    public DocumentEmbeddings Validate(int expectedCount, int dimensions)
    {
        if (Vectors.Count != expectedCount)
        {
            throw new EmbeddingException(
                $"The embedding provider returned {Vectors.Count} vectors for {expectedCount} texts.");
        }

        for (var i = 0; i < Vectors.Count; i++)
        {
            EmbeddingVectors.EnsureValid(Vectors[i], dimensions, $"vector {i}");
        }

        return TotalTokens >= 0 ? this : throw new EmbeddingException("The embedding provider reported negative token usage.");
    }
}

/// <summary>The vector for one question, and the tokens the provider billed for it.</summary>
public sealed record QueryEmbedding(float[] Vector, int TotalTokens)
{
    public QueryEmbedding Validate(int dimensions)
    {
        EmbeddingVectors.EnsureValid(Vector, dimensions, "the question's vector");
        return TotalTokens >= 0 ? this : throw new EmbeddingException("The embedding provider reported negative token usage.");
    }
}

/// <summary>
/// Embedding failed (provider down, rate-limited after retries, bad response). The processing pipeline retries
/// documents that fail this way; the message is for logs and the document's error, so keep it readable.
/// </summary>
public sealed class EmbeddingException(string message, Exception? innerException = null)
    : Exception(message, innerException);

internal static class EmbeddingVectors
{
    public static void EnsureValid(float[]? vector, int dimensions, string name)
    {
        if (vector is null || vector.Length != dimensions)
        {
            throw new EmbeddingException(
                $"The embedding provider returned {name} with {vector?.Length ?? 0} numbers instead of {dimensions}.");
        }

        foreach (var value in vector)
        {
            if (!float.IsFinite(value))
            {
                throw new EmbeddingException($"The embedding provider returned {name} containing an invalid number.");
            }
        }
    }
}
