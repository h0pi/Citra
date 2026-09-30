using System.ComponentModel.DataAnnotations;

namespace Citra.Infrastructure.Options;

/// <summary>How questions are matched with chunks. Section: "Retrieval".</summary>
public sealed class RetrievalOptions
{
    public const string SectionName = "Retrieval";

    /// <summary>
    /// Least cosine similarity a chunk found only by meaning needs to be used as a source. Depends on the embedding
    /// model: measured with voyage-4, related passages scored 0.39 to 0.60 (also for a question in Bosnian),
    /// unrelated ones mostly below 0.25 (at most 0.26). -1 keeps everything.
    /// </summary>
    [Range(-1.0, 1.0)]
    public double MinSimilarity { get; init; } = 0.25;

    /// <summary>
    /// Least similarity for chunks that also contain the question's words. Measured with voyage-4: a chunk about
    /// resetting passwords that also explains error ERR-4012 scored 0.246 for "What does ERR-4012 mean?".
    /// </summary>
    [Range(-1.0, 1.0)]
    public double MinKeywordSimilarity { get; init; } = 0.15;
}
