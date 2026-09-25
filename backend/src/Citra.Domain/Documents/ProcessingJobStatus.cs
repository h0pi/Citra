namespace Citra.Domain.Documents;

public enum ProcessingJobStatus
{
    /// <summary>Waiting to be picked up (new, or retrying after a failed attempt).</summary>
    Pending = 0,
    Running = 1,
    Succeeded = 2,

    /// <summary>Gave up after the maximum number of attempts.</summary>
    Failed = 3,
}
