using Citra.Application.Common.Embeddings;

namespace Citra.Application.Common.Retrieval;

/// <summary>
/// Hybrid search: runs the vector and the keyword search, each for more candidates than asked for, and combines
/// them with <see cref="ReciprocalRankFusion"/>. The keyword search runs while the question is being embedded.
/// Weak matches (less similar in meaning than the configured minimums, see <see cref="IRetrievalSettings"/>) are
/// dropped, and
/// overlapping neighbours among the best are joined (<see cref="NeighbourMerger"/>).
/// </summary>
internal sealed class Retriever(IEmbeddingService embeddings, IChunkSearch search, IRetrievalSettings settings) : IRetriever
{
    /// <summary>
    /// How many chunks each search returns before combining: a chunk ranked just outside the top of both lists
    /// can still be one of the best overall, so both look further than <c>topK</c>.
    /// </summary>
    public static int CandidatesFor(int topK) => Math.Max(topK * 3, 30);

    public async Task<RetrievalResult> SearchAsync(Guid botId, string query, int topK, CancellationToken cancellationToken)
    {
        if (botId == Guid.Empty)
        {
            throw new ArgumentException("Bot id is required.", nameof(botId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(topK, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(topK, IRetriever.MaxTopK);

        var text = query.Trim();
        var candidates = CandidatesFor(topK);

        // Embedding is a network call to the provider: search the keywords in the database meanwhile.
        var embedding = embeddings.EmbedQueryAsync(text, cancellationToken);
        var keyword = await search.KeywordAsync(botId, text, candidates, cancellationToken);
        var question = (await embedding).Validate(embeddings.Dimensions);
        var nearest = await search.NearestAsync(botId, question.Vector, candidates, cancellationToken);

        var fused = ReciprocalRankFusion.Fuse(nearest, keyword);

        // Keyword matches the vector search did not return have no similarity yet: look it up, so that meaning
        // decides for every chunk (with a lower bar for chunks that share the question's words). A chunk without
        // an embedding cannot be judged.
        var unknown = fused.Where(c => c.Similarity is null).Select(c => c.ChunkId).ToList();
        var similarities = await search.SimilaritiesAsync(botId, unknown, question.Vector, cancellationToken);

        var chunks = fused
            .Select(c => c.Similarity is null && similarities.TryGetValue(c.ChunkId, out var similarity)
                ? c with { Similarity = similarity }
                : c)
            .Where(c => c.Similarity >= (c.KeywordRank is null ? settings.MinSimilarity : settings.MinKeywordSimilarity))
            .Take(topK)
            .ToList();

        return new RetrievalResult(NeighbourMerger.Merge(chunks), question.TotalTokens);
    }
}
