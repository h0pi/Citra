namespace Citra.Application.Common.Retrieval;

/// <summary>
/// The database searches the retriever combines. Implemented in Infrastructure with PostgreSQL (pgvector and
/// full-text search), so the ranking itself happens where the data is.
/// </summary>
public interface IChunkSearch
{
    /// <summary>
    /// Vector search: the bot's chunks whose embeddings are closest in meaning to <paramref name="queryVector"/>
    /// (cosine distance), nearest first, at most <paramref name="limit"/>. Chunks without an embedding are skipped.
    /// Every hit has a <see cref="ChunkHit.Similarity"/>.
    /// </summary>
    Task<IReadOnlyList<ChunkHit>> NearestAsync(
        Guid botId, float[] queryVector, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Keyword search: the bot's chunks containing any of the words of <paramref name="query"/> (PostgreSQL
    /// full-text search: English stemming, so "refunds" finds "refund"; common words like "the" are ignored),
    /// best first, at most <paramref name="limit"/>. Chunks with more of the words, words that are closer
    /// together, or words in their heading rank higher. Every hit has a <see cref="ChunkHit.KeywordScore"/>.
    /// A query of only common words finds nothing.
    /// </summary>
    Task<IReadOnlyList<ChunkHit>> KeywordAsync(
        Guid botId, string query, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Cosine similarity of the given chunks of the bot to <paramref name="queryVector"/>, by chunk id. Chunks
    /// without an embedding (or of another bot) are left out. Used for keyword matches the vector search did not
    /// return, so every result can be held to the same minimum similarity.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, double>> SimilaritiesAsync(
        Guid botId, IReadOnlyCollection<Guid> chunkIds, float[] queryVector, CancellationToken cancellationToken);
}

/// <summary>A chunk one of the searches found, with the score that search gave it.</summary>
/// <param name="Similarity">Cosine similarity to the question (1 = same meaning, 0 = unrelated); vector search.</param>
/// <param name="KeywordScore">Full-text rank (PostgreSQL ts_rank, higher = better match); keyword search.</param>
public sealed record ChunkHit(
    Guid ChunkId,
    Guid DocumentId,
    string DocumentName,
    int Index,
    int? PageNumber,
    string? Heading,
    string Content,
    int CharStart,
    int CharEnd,
    double? Similarity,
    double? KeywordScore);
