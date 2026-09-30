namespace Citra.Application.Common.Retrieval;

/// <summary>
/// Combines the vector and keyword results into one ranking with Reciprocal Rank Fusion: every chunk scores
/// 1 / (<see cref="K"/> + rank) for each list it appears in, and the scores are added. Only positions count, so
/// the two searches' very different scores (cosine similarity vs ts_rank) never have to be compared, and a chunk
/// both searches found beats one that only a single search ranked high.
/// </summary>
public static class ReciprocalRankFusion
{
    /// <summary>The usual constant from the original paper: it keeps the first few places from dominating.</summary>
    public const int K = 60;

    /// <summary>
    /// All chunks of both lists, best first. Each list must be sorted best first. Ties (e.g. first place in only one
    /// list each) go to the chunk with the better single place, then to the better vector place (meaning is the
    /// stronger signal). Two different chunks never tie on all three, so the order is always the same.
    /// </summary>
    public static IReadOnlyList<RetrievedChunk> Fuse(IReadOnlyList<ChunkHit> vector, IReadOnlyList<ChunkHit> keyword)
    {
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentNullException.ThrowIfNull(keyword);

        var vectorRanks = Ranks(vector);
        var keywordRanks = Ranks(keyword);

        return vector.Concat(keyword)
            .DistinctBy(hit => hit.ChunkId)
            .Select(hit =>
            {
                int? vectorRank = vectorRanks.TryGetValue(hit.ChunkId, out var v) ? v.Rank : null;
                int? keywordRank = keywordRanks.TryGetValue(hit.ChunkId, out var k) ? k.Rank : null;
                var score = Contribution(vectorRank) + Contribution(keywordRank);
                return new RetrievedChunk(
                    hit.ChunkId, hit.DocumentId, hit.DocumentName, hit.Index, hit.PageNumber, hit.Heading, hit.Content,
                    hit.CharStart, hit.CharEnd, score,
                    Similarity: vectorRank is null ? null : v.Similarity,
                    KeywordScore: keywordRank is null ? null : k.KeywordScore,
                    vectorRank, keywordRank);
            })
            .OrderByDescending(c => c.Score)
            .ThenBy(c => Math.Min(c.VectorRank ?? int.MaxValue, c.KeywordRank ?? int.MaxValue))
            .ThenBy(c => c.VectorRank ?? int.MaxValue)
            .ToList();
    }

    private static double Contribution(int? rank) => rank is { } r ? 1.0 / (K + r) : 0;

    /// <summary>1-based position of each chunk in a list (the first time it appears).</summary>
    private static Dictionary<Guid, (int Rank, double? Similarity, double? KeywordScore)> Ranks(IReadOnlyList<ChunkHit> hits)
    {
        var ranks = new Dictionary<Guid, (int, double?, double?)>();
        for (var i = 0; i < hits.Count; i++)
        {
            ranks.TryAdd(hits[i].ChunkId, (i + 1, hits[i].Similarity, hits[i].KeywordScore));
        }

        return ranks;
    }
}
