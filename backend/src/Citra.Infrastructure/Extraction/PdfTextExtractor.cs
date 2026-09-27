using Citra.Application.Common.Extraction;
using Citra.Application.Common.Storage;
using Citra.Domain.Documents;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace Citra.Infrastructure.Extraction;

/// <summary>Reads PDFs with PdfPig, one entry per page, so citations can point to the right page.</summary>
internal sealed class PdfTextExtractor(IFileStorage storage) : FileTextExtractor(storage)
{
    /// <summary>Below this many letters/digits in the whole file, there is no real text layer.</summary>
    internal const int MinimumReadableCharacters = 20;

    internal const string ScannedMessage =
        "This PDF is a scanned image with no text layer. Reading scanned documents (OCR) is not supported yet; "
        + "export it as a text PDF or upload a Word version.";

    internal const string NoTextMessage = "This PDF contains no readable text.";

    internal const string PasswordMessage = "This PDF is password-protected. Remove the password and upload it again.";

    public override IReadOnlyCollection<DocumentSourceType> SourceTypes { get; } = [DocumentSourceType.Pdf];

    protected override string FormatName => "PDF";

    protected override async Task<ExtractedDocument> ExtractCoreAsync(Stream content, CancellationToken cancellationToken)
    {
        await using var buffer = await BufferAsync(content, cancellationToken);

        PdfDocument pdf;
        try
        {
            // Lenient: many real-world PDFs break the spec slightly but are still readable.
            pdf = PdfDocument.Open(buffer, new ParsingOptions { UseLenientParsing = true });
        }
        catch (PdfDocumentEncryptedException ex)
        {
            throw new TextExtractionException(PasswordMessage, ex);
        }

        using (pdf)
        {
            var pages = new List<ExtractedPage>(pdf.NumberOfPages);
            var readableCharacters = 0;
            var hasImages = false;

            foreach (var page in pdf.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Content order keeps words, line breaks and reading order much closer to the page than raw text.
                var text = ContentOrderTextExtractor.GetText(page);
                readableCharacters += text.Count(char.IsLetterOrDigit);
                hasImages |= page.GetImages().Any();
                pages.Add(new ExtractedPage(page.Number, text));
            }

            if (readableCharacters < MinimumReadableCharacters)
            {
                throw new TextExtractionException(hasImages ? ScannedMessage : NoTextMessage);
            }

            return new ExtractedDocument(pages);
        }
    }
}
