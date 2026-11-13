using Citra.Domain.Documents;

namespace Citra.IntegrationTests.Processing;

/// <summary>
/// Extraction may (re)start from any unfinished status — a retry or a crash takeover begins again from the
/// first step — but never from a finished one: Ready/Failed documents go through ResetForReprocessing first.
/// </summary>
public sealed class DocumentStatusTransitionTests
{
    [Theory]
    [InlineData(DocumentStatus.Uploaded, true)]
    [InlineData(DocumentStatus.Extracting, true)]
    [InlineData(DocumentStatus.Chunking, true)]
    [InlineData(DocumentStatus.Embedding, true)]
    [InlineData(DocumentStatus.Ready, false)]
    [InlineData(DocumentStatus.Failed, false)]
    public void Extraction_starts_from_any_unfinished_status(DocumentStatus from, bool allowed)
    {
        var document = DocumentIn(from);

        if (allowed)
        {
            document.StartExtracting();
            document.Status.ShouldBe(DocumentStatus.Extracting);
        }
        else
        {
            Should.Throw<InvalidOperationException>(document.StartExtracting).Message.ShouldContain($"from {from} to Extracting");
            document.Status.ShouldBe(from);
        }
    }

    [Fact]
    public void Every_status_is_covered()
    {
        Enum.GetValues<DocumentStatus>().Length.ShouldBe(6, "a new status needs a decision in the test above");
    }

    private static Document DocumentIn(DocumentStatus status)
    {
        var document = Document.FromUpload(
            Guid.NewGuid(), Guid.NewGuid(), "a.pdf", "application/pdf", 10, DocumentSourceType.Pdf, "p/original.pdf", new string('a', 64));
        var now = DateTimeOffset.UtcNow;

        switch (status)
        {
            case DocumentStatus.Uploaded:
                break;
            case DocumentStatus.Extracting:
                document.StartExtracting();
                break;
            case DocumentStatus.Chunking:
                document.StartExtracting();
                document.StartChunking();
                break;
            case DocumentStatus.Embedding:
                document.StartExtracting();
                document.StartChunking();
                document.StartEmbedding();
                break;
            case DocumentStatus.Ready:
                document.StartExtracting();
                document.StartChunking();
                document.StartEmbedding();
                document.MarkReady(1, 1, now);
                break;
            case DocumentStatus.Failed:
                document.MarkFailed("broken", now);
                break;
        }

        document.Status.ShouldBe(status);
        return document;
    }
}
