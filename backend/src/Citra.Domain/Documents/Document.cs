using Citra.Domain.Common;

namespace Citra.Domain.Documents;

/// <summary>
/// A file or web page a bot answers from. Its status only changes through the methods below,
/// which enforce the processing order (you can't mark a document Ready while it is still Extracting, etc.).
/// </summary>
public sealed class Document : Entity, ITenantOwned
{
    public const int FileNameMaxLength = 255;
    public const int ContentTypeMaxLength = 100;
    public const int SourceUrlMaxLength = 2048;
    public const int StoragePathMaxLength = 500;
    public const int ContentHashLength = 64; // SHA-256 as hex
    public const int ErrorMessageMaxLength = 2000;

    public Guid TenantId { get; private set; }

    public Guid BotId { get; private set; }

    /// <summary>Original file name, or the page title for URLs.</summary>
    public string FileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public DocumentSourceType SourceType { get; private set; }

    public string? SourceUrl { get; private set; }

    /// <summary>Where the original file is stored (relative path in file storage). Null for URLs.</summary>
    public string? StoragePath { get; private set; }

    /// <summary>SHA-256 of the file content; used to reject duplicate uploads within a bot.</summary>
    public string? ContentHash { get; private set; }

    public DocumentStatus Status { get; private set; } = DocumentStatus.Uploaded;

    public string? ErrorMessage { get; private set; }

    public int PageCount { get; private set; }

    public int ChunkCount { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    private Document()
    {
        // For EF Core.
    }

    public static Document FromUpload(
        Guid tenantId,
        Guid botId,
        string fileName,
        string contentType,
        long sizeBytes,
        DocumentSourceType sourceType,
        string storagePath,
        string contentHash)
    {
        EnsureIds(tenantId, botId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeBytes);
        if (sourceType == DocumentSourceType.Url)
        {
            throw new ArgumentException("Use FromUrl for web pages.", nameof(sourceType));
        }

        return new Document
        {
            TenantId = tenantId,
            BotId = botId,
            FileName = fileName.Trim(),
            ContentType = contentType,
            SizeBytes = sizeBytes,
            SourceType = sourceType,
            StoragePath = storagePath,
            ContentHash = contentHash,
        };
    }

    public static Document FromUrl(Guid tenantId, Guid botId, Uri url)
    {
        EnsureIds(tenantId, botId);
        ArgumentNullException.ThrowIfNull(url);
        if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Only http and https URLs are supported.", nameof(url));
        }

        return new Document
        {
            TenantId = tenantId,
            BotId = botId,
            FileName = url.Host + url.AbsolutePath.TrimEnd('/'),
            ContentType = "text/html",
            SourceType = DocumentSourceType.Url,
            SourceUrl = url.ToString(),
        };
    }

    // ---------- Processing pipeline ----------

    public void StartExtracting() => MoveTo(DocumentStatus.Extracting, from: [DocumentStatus.Uploaded]);

    public void StartChunking() => MoveTo(DocumentStatus.Chunking, from: [DocumentStatus.Extracting]);

    public void StartEmbedding() => MoveTo(DocumentStatus.Embedding, from: [DocumentStatus.Chunking]);

    public void MarkReady(int pageCount, int chunkCount, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageCount);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkCount);

        MoveTo(DocumentStatus.Ready, from: [DocumentStatus.Embedding]);
        PageCount = pageCount;
        ChunkCount = chunkCount;
        ProcessedAt = now;
    }

    /// <summary>Can happen at any step. The message is shown to the user, so keep it human-readable.</summary>
    public void MarkFailed(string errorMessage, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        Status = DocumentStatus.Failed;
        ErrorMessage = errorMessage.Length > ErrorMessageMaxLength
            ? errorMessage[..ErrorMessageMaxLength]
            : errorMessage;
        ProcessedAt = now;
    }

    /// <summary>Sends the document through the pipeline again (after a failure, or to re-embed with a new model).</summary>
    public void ResetForReprocessing()
    {
        Status = DocumentStatus.Uploaded;
        ErrorMessage = null;
        PageCount = 0;
        ChunkCount = 0;
        ProcessedAt = null;
    }

    public void SetTitle(string title)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            FileName = title.Trim().Length > FileNameMaxLength
                ? title.Trim()[..FileNameMaxLength]
                : title.Trim();
        }
    }

    private void MoveTo(DocumentStatus next, DocumentStatus[] from)
    {
        if (!from.Contains(Status))
        {
            throw new InvalidOperationException(
                $"Document {Id} cannot move from {Status} to {next}.");
        }

        Status = next;
    }

    private static void EnsureIds(Guid tenantId, Guid botId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (botId == Guid.Empty)
        {
            throw new ArgumentException("Bot id is required.", nameof(botId));
        }
    }
}
