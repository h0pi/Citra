using Citra.Domain.Documents;

namespace Citra.Application.Common.Extraction;

/// <summary>
/// Reads the text out of one kind of document (PDF, Word, plain text, web page), page by page, so that
/// answers can later cite "page 7". Pick the right one with <see cref="ITextExtractorFactory"/>.
/// </summary>
public interface ITextExtractor
{
    /// <summary>The document types this extractor reads.</summary>
    IReadOnlyCollection<DocumentSourceType> SourceTypes { get; }

    /// <summary>
    /// Extracts the raw text (see <see cref="TextCleaner"/> for the tidy-up that follows).
    /// Throws <see cref="TextExtractionException"/> with a message for the user when the document
    /// cannot be read (damaged file, scanned PDF, page not found, ...).
    /// </summary>
    Task<ExtractedDocument> ExtractAsync(Document document, CancellationToken cancellationToken);
}

/// <summary>Chooses the extractor for a document type.</summary>
public interface ITextExtractorFactory
{
    ITextExtractor For(DocumentSourceType sourceType);
}

/// <summary>One page of text. Formats without real pages (Word, text, web) return a single page 1.</summary>
public sealed record ExtractedPage(int PageNumber, string Text);

/// <summary>
/// The text of a document. <see cref="Title"/> is set when the source has a better name than the file name
/// (e.g. a web page's &lt;title&gt;), so the document list can show it.
/// </summary>
public sealed record ExtractedDocument(IReadOnlyList<ExtractedPage> Pages, string? Title = null)
{
    public bool HasText => Pages.Any(p => !string.IsNullOrWhiteSpace(p.Text));

    public int CharacterCount => Pages.Sum(p => p.Text.Length);
}

/// <summary>
/// The document could not be read, for a reason the user can act on. <see cref="Exception.Message"/> is shown
/// in the documents list as the document's error (via Document.MarkFailed), so keep it short and human.
/// </summary>
public sealed class TextExtractionException(string message, Exception? innerException = null)
    : Exception(message, innerException);
