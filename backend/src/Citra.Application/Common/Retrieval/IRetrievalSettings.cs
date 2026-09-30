namespace Citra.Application.Common.Retrieval;

/// <summary>Search rules that come from configuration (section "Retrieval").</summary>
public interface IRetrievalSettings
{
    /// <summary>
    /// Chunks found only by meaning must be at least this similar to the question, or they are dropped: better no
    /// source than an unrelated one the model might answer from. Cosine similarity from -1 (keep all) to 1.
    /// </summary>
    double MinSimilarity { get; }

    /// <summary>
    /// The (lower) minimum for chunks that also contain the question's words: sharing an exact term (a code, a
    /// name) is evidence of its own, e.g. a chunk mostly about passwords that also explains error "ERR-4012".
    /// </summary>
    double MinKeywordSimilarity { get; }
}
