using Citra.Domain.Common;

namespace Citra.Domain.Documents;

/// <summary>
/// A searchable piece of a document (~500 tokens) with its embedding vector.
/// Page number and character offsets let the UI highlight the exact cited passage.
/// </summary>
public sealed class Chunk : Entity
{
    public const int HeadingMaxLength = 300;

    /// <summary>
    /// Size of every embedding vector. Fixed by the database column (vector(1024)); changing the
    /// embedding model to a different size requires a migration and re-processing all documents.
    /// </summary>
    public const int EmbeddingDimensions = 1024;

    public Guid DocumentId { get; private set; }

    /// <summary>Denormalized from the document so vector search can filter by bot without a join.</summary>
    public Guid BotId { get; private set; }

    /// <summary>Position of the chunk within its document (0, 1, 2, ...).</summary>
    public int Index { get; private set; }

    public string Content { get; private set; } = string.Empty;

    /// <summary>Section heading the chunk belongs to, used as extra context for search and answers.</summary>
    public string? Heading { get; private set; }

    /// <summary>1-based page number (PDF/DOCX); null for sources without pages.</summary>
    public int? PageNumber { get; private set; }

    /// <summary>Start/end character offsets of this chunk within its page's text.</summary>
    public int CharStart { get; private set; }

    public int CharEnd { get; private set; }

    public int TokenCount { get; private set; }

    /// <summary>
    /// Embedding vector (e.g. 1024 floats). Plain float[] keeps the domain free of database types;
    /// the persistence layer maps it to a pgvector column. Null until the embedding step runs.
    /// </summary>
    public float[]? Embedding { get; private set; }

    private Chunk()
    {
        // For EF Core.
    }

    public static Chunk Create(
        Guid documentId,
        Guid botId,
        int index,
        string content,
        string? heading,
        int? pageNumber,
        int charStart,
        int charEnd,
        int tokenCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(charStart);
        ArgumentOutOfRangeException.ThrowIfLessThan(charEnd, charStart);
        ArgumentOutOfRangeException.ThrowIfNegative(tokenCount);
        if (pageNumber is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "Page numbers start at 1.");
        }

        return new Chunk
        {
            DocumentId = documentId,
            BotId = botId,
            Index = index,
            Content = content,
            Heading = string.IsNullOrWhiteSpace(heading) ? null : heading.Trim(),
            PageNumber = pageNumber,
            CharStart = charStart,
            CharEnd = charEnd,
            TokenCount = tokenCount,
        };
    }

    public void SetEmbedding(float[] embedding)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        if (embedding.Length != EmbeddingDimensions)
        {
            throw new ArgumentException(
                $"Embedding must have {EmbeddingDimensions} dimensions, got {embedding.Length}.",
                nameof(embedding));
        }

        Embedding = embedding;
    }
}
