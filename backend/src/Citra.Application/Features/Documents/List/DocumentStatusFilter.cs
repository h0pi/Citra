using Citra.Domain.Documents;

namespace Citra.Application.Features.Documents.List;

/// <summary>
/// Status groups as users think about them. The three internal steps (Extracting, Chunking, Embedding)
/// are all just "Processing" in the UI.
/// </summary>
public enum DocumentStatusFilter
{
    /// <summary>Uploaded, waiting for the Worker.</summary>
    Queued,
    Processing,
    Ready,
    Failed,
}

internal static class DocumentStatusFilterExtensions
{
    public static DocumentStatus[] Statuses(this DocumentStatusFilter filter) =>
        filter switch
        {
            DocumentStatusFilter.Queued => [DocumentStatus.Uploaded],
            DocumentStatusFilter.Processing => [DocumentStatus.Extracting, DocumentStatus.Chunking, DocumentStatus.Embedding],
            DocumentStatusFilter.Ready => [DocumentStatus.Ready],
            DocumentStatusFilter.Failed => [DocumentStatus.Failed],
            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null),
        };
}
