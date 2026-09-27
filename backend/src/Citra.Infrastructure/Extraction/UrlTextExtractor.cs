using Citra.Application.Common.Extraction;
using Citra.Domain.Documents;

namespace Citra.Infrastructure.Extraction;

/// <summary>
/// Downloads a web page (safely, see <see cref="WebPageFetcher"/>) and keeps its main content as one page.
/// The page's &lt;title&gt; becomes the document's name in the list.
/// </summary>
internal sealed class UrlTextExtractor(WebPageFetcher fetcher) : ITextExtractor
{
    public IReadOnlyCollection<DocumentSourceType> SourceTypes { get; } = [DocumentSourceType.Url];

    public async Task<ExtractedDocument> ExtractAsync(Document document, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(document.SourceUrl, UriKind.Absolute, out var url))
        {
            throw new TextExtractionException("This document has no valid web address.");
        }

        var page = await fetcher.FetchAsync(url, cancellationToken);

        var (text, title) = page.MediaType == "text/plain"
            ? (PlainTextExtractor.Decode(page.Content, page.Charset), null)
            : HtmlContentReader.Read(page.Content, page.Charset);

        if (text.Count(char.IsLetterOrDigit) < PdfTextExtractor.MinimumReadableCharacters)
        {
            // Typical for sites that build the page with JavaScript in the browser.
            throw new TextExtractionException(
                "No readable text was found on this page. It may need JavaScript to show its content; "
                + "copy the text into a .txt file and upload that instead.");
        }

        return new ExtractedDocument([new ExtractedPage(1, text)], title);
    }
}
