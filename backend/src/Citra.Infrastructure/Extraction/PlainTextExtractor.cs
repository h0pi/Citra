using System.Text;
using Citra.Application.Common.Extraction;
using Citra.Application.Common.Storage;
using Citra.Domain.Documents;

namespace Citra.Infrastructure.Extraction;

/// <summary>
/// Reads .txt and .md files. Markdown is kept as it is: its "#" headings are exactly what the chunker splits on.
/// Text files have no pages, so everything is page 1.
/// </summary>
internal sealed class PlainTextExtractor(IFileStorage storage) : FileTextExtractor(storage)
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static PlainTextExtractor()
    {
        // Makes Windows-1250 (Central European: č ć š ž đ) available; .NET only ships UTF-* and Latin-1 by default.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public override IReadOnlyCollection<DocumentSourceType> SourceTypes { get; } =
        [DocumentSourceType.Text, DocumentSourceType.Markdown];

    protected override string FormatName => "text";

    protected override async Task<ExtractedDocument> ExtractCoreAsync(Stream content, CancellationToken cancellationToken)
    {
        await using var buffer = await BufferAsync(content, cancellationToken);
        return new ExtractedDocument([new ExtractedPage(1, Decode(buffer.ToArray()))]);
    }

    /// <summary>
    /// Bytes → text: a byte-order mark wins; otherwise UTF-8 (what nearly all modern files use), falling back to
    /// the declared charset (web pages) or Windows-1250 for older Windows files typed in Bosnian, Croatian, Serbian.
    /// </summary>
    internal static string Decode(byte[] bytes, string? declaredCharset = null)
    {
        var bomEncoding = DetectBom(bytes, out var bomLength);
        if (bomEncoding is not null)
        {
            return bomEncoding.GetString(bytes, bomLength, bytes.Length - bomLength);
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Not UTF-8: fall through to a single-byte encoding.
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(declaredCharset))
            {
                return Encoding.GetEncoding(declaredCharset.Trim('"', ' ')).GetString(bytes);
            }
        }
        catch (ArgumentException)
        {
            // Unknown charset name: use the fallback below.
        }

        return Encoding.GetEncoding(1250).GetString(bytes);
    }

    private static Encoding? DetectBom(byte[] bytes, out int length)
    {
        (Encoding? encoding, length) = bytes switch
        {
            [0xEF, 0xBB, 0xBF, ..] => ((Encoding?)Encoding.UTF8, 3),
            [0xFF, 0xFE, ..] => (Encoding.Unicode, 2),
            [0xFE, 0xFF, ..] => (Encoding.BigEndianUnicode, 2),
            _ => (null, 0),
        };
        return encoding;
    }
}
