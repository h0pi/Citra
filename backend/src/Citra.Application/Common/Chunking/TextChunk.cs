using Citra.Domain.Documents;

namespace Citra.Application.Common.Chunking;

/// <summary>
/// One searchable piece of a document, as the chunker produces it (saved as a <see cref="Chunk"/> entity).
/// <see cref="Content"/> is always exactly <c>page.Text[CharStart..CharEnd]</c> of its page's cleaned text,
/// so a citation can point at and highlight the original passage. Offsets count UTF-16 code units, the same
/// as C# and JavaScript string indexes, so the browser can use them unchanged.
/// </summary>
/// <param name="Index">Position in the document: 0, 1, 2, ...</param>
/// <param name="Content">The chunk's text, cut from one page.</param>
/// <param name="Heading">Section path, e.g. "Vacation › Carry-over"; null before the first heading.</param>
/// <param name="PageNumber">1-based page for paged sources (PDF, Word); null for text files and web pages.</param>
/// <param name="CharStart">Start of <see cref="Content"/> in its page's text.</param>
/// <param name="CharEnd">End (exclusive) of <see cref="Content"/> in its page's text.</param>
/// <param name="TokenCount">Tokens in <see cref="Content"/> (the size limits apply to this).</param>
/// <param name="EmbeddingText">
/// What gets embedded: <see cref="Content"/> with the document title and section in front (see <see cref="ChunkContext"/>).
/// Not stored; rebuilt with <see cref="ChunkContext.ForEmbedding"/> from the saved chunk when needed.
/// </param>
public sealed record TextChunk(
    int Index,
    string Content,
    string? Heading,
    int? PageNumber,
    int CharStart,
    int CharEnd,
    int TokenCount,
    string EmbeddingText)
{
    /// <summary>The database entity for this chunk (its embedding is added later by the embedding step).</summary>
    public Chunk ToEntity(Guid documentId, Guid botId) =>
        Chunk.Create(documentId, botId, Index, Content, Heading, PageNumber, CharStart, CharEnd, TokenCount);
}

/// <summary>Chunk sizes, in tokens.</summary>
/// <param name="TargetTokens">Chunks are filled up to about this size (overlap included).</param>
/// <param name="MaxTokens">No chunk is ever bigger than this.</param>
/// <param name="OverlapTokens">How much of the previous chunk (same page and section) a chunk repeats at its start.</param>
public sealed record ChunkingOptions(int TargetTokens = 500, int MaxTokens = 800, int OverlapTokens = 80)
{
    public static ChunkingOptions Default { get; } = new();

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(TargetTokens, 20, nameof(TargetTokens));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxTokens, TargetTokens, nameof(MaxTokens));
        ArgumentOutOfRangeException.ThrowIfNegative(OverlapTokens, nameof(OverlapTokens));

        // Overlap must leave most of each chunk for new text, or splitting would crawl forward.
        ArgumentOutOfRangeException.ThrowIfGreaterThan(OverlapTokens, TargetTokens / 2, nameof(OverlapTokens));
    }
}
