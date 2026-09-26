using Citra.Domain.Documents;

namespace Citra.Application.Features.Documents.Upload;

/// <summary>
/// The file types Citra can read, recognised by extension and then confirmed by content ("magic bytes"),
/// so a renamed .exe or an image saved as .pdf is rejected at upload instead of failing later in processing.
/// The stored content type is ours, never the browser's (which is often wrong or missing).
/// </summary>
public static class SupportedFileTypes
{
    /// <summary>How many leading bytes <see cref="LooksLike"/> needs to decide.</summary>
    public const int HeaderLength = 8 * 1024;

    public sealed record FileType(DocumentSourceType SourceType, string ContentType, string Description);

    private static readonly FileType Pdf = new(DocumentSourceType.Pdf, "application/pdf", "PDF");

    private static readonly FileType Docx = new(
        DocumentSourceType.Docx,
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "Word (.docx)");

    private static readonly FileType Text = new(DocumentSourceType.Text, "text/plain", "text");
    private static readonly FileType Markdown = new(DocumentSourceType.Markdown, "text/markdown", "Markdown");

    private static readonly Dictionary<string, FileType> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = Pdf,
        [".docx"] = Docx,
        [".txt"] = Text,
        [".md"] = Markdown,
        [".markdown"] = Markdown,
    };

    /// <summary>For error messages: ".pdf, .docx, .txt, .md".</summary>
    public const string AllowedList = ".pdf, .docx, .txt, .md";

    public static FileType? FromFileName(string fileName) =>
        ByExtension.GetValueOrDefault(Path.GetExtension((fileName ?? string.Empty).Trim()));

    /// <summary>True if the file's first bytes match what its type must look like.</summary>
    public static bool LooksLike(FileType type, ReadOnlySpan<byte> header) =>
        type.SourceType switch
        {
            // Every PDF starts with "%PDF-" (a few tools put junk before it; readers accept it within 1 KB).
            DocumentSourceType.Pdf => header[..Math.Min(header.Length, 1024)].IndexOf("%PDF-"u8) >= 0,

            // .docx is a ZIP archive: "PK\x03\x04".
            DocumentSourceType.Docx => header.StartsWith("PK\u0003\u0004"u8),

            // Text files must not contain NUL bytes: those only appear in binary files
            // (or UTF-16 text, which the text extractor does not read).
            DocumentSourceType.Text or DocumentSourceType.Markdown => header.IndexOf((byte)0) < 0,

            _ => false,
        };
}
