using Citra.Application.Common.Extraction;
using Citra.Application.Common.Storage;
using Citra.Domain.Documents;

namespace Citra.Infrastructure.Extraction;

/// <summary>
/// Base for extractors that read an uploaded file: opens it from storage and turns library errors about
/// broken files into a message the user can act on. Subclasses only parse a stream (which tests call directly).
/// </summary>
internal abstract class FileTextExtractor(IFileStorage storage) : ITextExtractor
{
    public abstract IReadOnlyCollection<DocumentSourceType> SourceTypes { get; }

    /// <summary>Used in messages: "This PDF file could not be read ...".</summary>
    protected abstract string FormatName { get; }

    public async Task<ExtractedDocument> ExtractAsync(Document document, CancellationToken cancellationToken)
    {
        if (document.StoragePath is null)
        {
            throw new TextExtractionException("This document has no stored file. Upload it again.");
        }

        await using var content = await storage.OpenReadAsync(document.StoragePath, cancellationToken)
            ?? throw new TextExtractionException("The file of this document is missing. Upload it again.");

        return await ExtractAsync(content, cancellationToken);
    }

    /// <summary>Extracts text from the file's bytes.</summary>
    public async Task<ExtractedDocument> ExtractAsync(Stream content, CancellationToken cancellationToken)
    {
        try
        {
            return await ExtractCoreAsync(content, cancellationToken);
        }
        catch (Exception ex) when (ex is not (TextExtractionException or OperationCanceledException))
        {
            throw new TextExtractionException(
                $"This {FormatName} file could not be read. It may be damaged or in an unsupported format.", ex);
        }
    }

    protected abstract Task<ExtractedDocument> ExtractCoreAsync(Stream content, CancellationToken cancellationToken);

    /// <summary>The PDF and Word libraries need to jump around in the file; uploads are at most 20 MB.</summary>
    protected static async Task<MemoryStream> BufferAsync(Stream content, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return buffer;
    }
}
