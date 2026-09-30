namespace Citra.Application.Common.Retrieval;

/// <summary>
/// Consecutive chunks of one page share text: each repeats the end of the one before (the chunker's overlap). When
/// a search finds both, they are joined into one passage, so the model does not read the same sentences twice and
/// one citation covers the whole passage. Only chunks that really overlap are joined: neighbours that merely touch
/// (a new section starts there) share no text.
/// </summary>
public static class NeighbourMerger
{
    /// <summary>
    /// The ranked chunks with overlapping neighbours joined. A joined passage takes the place of its best-ranked
    /// part and keeps each search's best score and place; its <see cref="RetrievedChunk.ChunkId"/> and
    /// <see cref="RetrievedChunk.Index"/> are its first chunk's (in reading order).
    /// </summary>
    public static IReadOnlyList<RetrievedChunk> Merge(IReadOnlyList<RetrievedChunk> ranked)
    {
        ArgumentNullException.ThrowIfNull(ranked);

        // Which passage each chunk ends up in: runs of overlapping neighbours, per document page.
        var passageOf = new Dictionary<Guid, RetrievedChunk>();
        foreach (var page in ranked.GroupBy(c => (c.DocumentId, c.PageNumber)))
        {
            var run = new List<RetrievedChunk>();
            foreach (var chunk in page.OrderBy(c => c.Index))
            {
                if (run.Count > 0 && !Overlaps(run[^1], chunk))
                {
                    Close(run, passageOf);
                }

                run.Add(chunk);
            }

            Close(run, passageOf);
        }

        // Every passage once, where its best part was ranked.
        return ranked
            .Select(c => passageOf[c.ChunkId])
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// The next chunk of the same page starts before the previous one ends (so they share text). Both must hold
    /// exactly their page's text between their offsets, as the chunker guarantees, or they are left alone.
    /// </summary>
    private static bool Overlaps(RetrievedChunk previous, RetrievedChunk next) =>
        next.Index == previous.Index + 1
        && next.CharStart < previous.CharEnd
        && next.CharStart >= previous.CharStart
        && HasExactOffsets(previous)
        && HasExactOffsets(next);

    private static bool HasExactOffsets(RetrievedChunk chunk) => chunk.Content.Length == chunk.CharEnd - chunk.CharStart;

    private static void Close(List<RetrievedChunk> run, Dictionary<Guid, RetrievedChunk> passageOf)
    {
        if (run.Count == 0)
        {
            return;
        }

        var passage = run[0];
        foreach (var next in run.Skip(1))
        {
            // The page's text from the passage's start to the later end of the two.
            var content = next.CharEnd > passage.CharEnd
                ? passage.Content + next.Content[(passage.CharEnd - next.CharStart)..]
                : passage.Content;
            passage = passage with
            {
                Content = content,
                CharEnd = Math.Max(passage.CharEnd, next.CharEnd),
                Score = Math.Max(passage.Score, next.Score),
                Similarity = Max(passage.Similarity, next.Similarity),
                KeywordScore = Max(passage.KeywordScore, next.KeywordScore),
                VectorRank = Min(passage.VectorRank, next.VectorRank),
                KeywordRank = Min(passage.KeywordRank, next.KeywordRank),
                ChunkCount = passage.ChunkCount + next.ChunkCount,
            };
        }

        foreach (var chunk in run)
        {
            passageOf[chunk.ChunkId] = passage;
        }

        run.Clear();
    }

    private static double? Max(double? a, double? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);

    private static int? Min(int? a, int? b) => a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);
}
