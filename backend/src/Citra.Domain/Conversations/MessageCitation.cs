using Citra.Domain.Common;

namespace Citra.Domain.Conversations;

/// <summary>
/// A source an answer cited as [1], [2], ...
/// Keeps a copy of the document name, page and quoted snippet, so old answers still show their sources
/// even after the document was re-processed (new chunks) or deleted (links become null).
/// </summary>
public sealed class MessageCitation : Entity
{
    public const int DocumentNameMaxLength = 255;
    public const int SnippetMaxLength = 2000;

    public Guid MessageId { get; private set; }

    /// <summary>The [n] marker number in the answer text.</summary>
    public int Number { get; private set; }

    public Guid? ChunkId { get; private set; }

    public Guid? DocumentId { get; private set; }

    public string DocumentName { get; private set; } = string.Empty;

    public int? PageNumber { get; private set; }

    public string Snippet { get; private set; } = string.Empty;

    /// <summary>Retrieval relevance score of the cited chunk.</summary>
    public double Score { get; private set; }

    private MessageCitation()
    {
        // For EF Core.
    }

    internal static MessageCitation Create(
        Guid messageId,
        int number,
        Guid? chunkId,
        Guid? documentId,
        string documentName,
        int? pageNumber,
        string snippet,
        double score)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(snippet);

        return new MessageCitation
        {
            MessageId = messageId,
            Number = number,
            ChunkId = chunkId,
            DocumentId = documentId,
            DocumentName = documentName.Length > DocumentNameMaxLength ? documentName[..DocumentNameMaxLength] : documentName,
            PageNumber = pageNumber,
            Snippet = snippet.Length > SnippetMaxLength ? snippet[..SnippetMaxLength] : snippet,
            Score = score,
        };
    }
}
