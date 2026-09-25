namespace Citra.Domain.Documents;

public enum DocumentSourceType
{
    Pdf = 0,
    Docx = 1,
    Text = 2,
    Markdown = 3,

    /// <summary>A web page fetched from <see cref="Document.SourceUrl"/>.</summary>
    Url = 4,
}
