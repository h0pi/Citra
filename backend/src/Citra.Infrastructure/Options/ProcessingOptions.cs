using System.ComponentModel.DataAnnotations;

namespace Citra.Infrastructure.Options;

/// <summary>How Workers find and take document-processing jobs. Section: "Processing".</summary>
public sealed class ProcessingOptions
{
    public const string SectionName = "Processing";

    /// <summary>
    /// Pause between checks of the job table when there was nothing to do. Short enough that an upload starts
    /// processing almost at once, long enough that an idle Worker barely touches the database.
    /// </summary>
    [Range(100, 60_000)]
    public int PollIntervalMilliseconds { get; init; } = 2000;

    /// <summary>
    /// How long a taken job belongs to its worker. If the worker crashes, another one takes the job over after
    /// this time; so it must be longer than processing a big document takes (on Voyage's free tier, embedding
    /// runs at 3 requests per minute), but short enough that a crash does not stall a document for long.
    /// </summary>
    [Range(1, 1440)]
    public int LockDurationMinutes { get; init; } = 15;

    public TimeSpan PollInterval => TimeSpan.FromMilliseconds(PollIntervalMilliseconds);

    public TimeSpan LockDuration => TimeSpan.FromMinutes(LockDurationMinutes);
}
