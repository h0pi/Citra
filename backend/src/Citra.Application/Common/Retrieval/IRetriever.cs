namespace Citra.Application.Common.Retrieval;

/// <summary>
/// Finds the chunks of a bot's documents that best answer a question: the "R" in RAG. The chat sends the best
/// ones to the model as numbered sources, so it can answer from them and cite them. Hybrid: chunks close in meaning
/// (vector search) and chunks sharing the question's words (keyword search: names, codes, exact terms), combined.
/// </summary>
/// <remarks>
/// Searches only the given bot's chunks and does not check who is asking: callers must first make sure the
/// bot is one the current user (or the website widget's key) may use.
/// </remarks>
public interface IRetriever
{
    /// <summary>Most results one search can return.</summary>
    const int MaxTopK = 50;

    /// <summary>
    /// The (at most) <paramref name="topK"/> chunks most relevant to <paramref name="query"/>, best first; none
    /// less similar in meaning than the configured minimum. Overlapping neighbours among them come back joined into
    /// one passage, so there can be fewer.
    /// The question is embedded once (the tokens are returned, so the caller can record what the search cost).
    /// </summary>
    /// <exception cref="ArgumentException">Empty bot id, blank query, or topK outside 1..<see cref="MaxTopK"/>.</exception>
    Task<RetrievalResult> SearchAsync(Guid botId, string query, int topK, CancellationToken cancellationToken);
}

/// <summary>The chunks a search found, best first, and the tokens the question's embedding cost.</summary>
public sealed record RetrievalResult(IReadOnlyList<RetrievedChunk> Chunks, int QueryTokens);

/// <summary>A chunk a search found, with what an answer needs to cite it (document, page, exact passage).</summary>
/// <param name="Index">Position of the chunk in its document: 0, 1, 2, …</param>
/// <param name="PageNumber">1-based page for PDF/Word; null for text files and web pages.</param>
/// <param name="CharStart">Where <paramref name="Content"/> starts in its page's text (for highlighting).</param>
/// <param name="Score">Combined relevance (see <see cref="ReciprocalRankFusion"/>); higher is better.</param>
/// <param name="Similarity">Cosine similarity to the question (always set in a retriever's results).</param>
/// <param name="KeywordScore">Full-text rank, when the keyword search found the chunk.</param>
/// <param name="VectorRank">1-based place in the vector search's results, if there.</param>
/// <param name="KeywordRank">1-based place in the keyword search's results, if there.</param>
/// <param name="ChunkCount">How many overlapping neighbour chunks this passage joins (see <see cref="NeighbourMerger"/>).</param>
public sealed record RetrievedChunk(
    Guid ChunkId,
    Guid DocumentId,
    string DocumentName,
    int Index,
    int? PageNumber,
    string? Heading,
    string Content,
    int CharStart,
    int CharEnd,
    double Score,
    double? Similarity,
    double? KeywordScore,
    int? VectorRank,
    int? KeywordRank,
    int ChunkCount = 1);
