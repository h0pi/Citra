namespace Citra.Domain.Documents;

/// <summary>Processing pipeline: Uploaded → Extracting → Chunking → Embedding → Ready (or Failed at any step).</summary>
public enum DocumentStatus
{
    Uploaded = 0,
    Extracting = 1,
    Chunking = 2,
    Embedding = 3,
    Ready = 4,
    Failed = 5,
}
