using System.ComponentModel.DataAnnotations;
using Citra.Domain.Documents;

namespace Citra.Infrastructure.Options;

/// <summary>Settings for Voyage AI embeddings. Section: "Voyage".</summary>
public sealed class VoyageOptions : IValidatableObject
{
    public const string SectionName = "Voyage";

    /// <summary>
    /// Not required at startup: the key is only needed once embeddings are implemented and used.
    /// The Voyage client throws a clear error if it is called without one.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    [Required, Url]
    public string BaseUrl { get; init; } = string.Empty;

    [Required]
    public string EmbeddingModel { get; init; } = string.Empty;

    /// <summary>Must match the pgvector column size, e.g. vector(1024).</summary>
    [Range(256, 4096)]
    public int EmbeddingDimensions { get; init; }

    /// <summary>At most this many texts per request (Voyage allows 1,000).</summary>
    [Range(1, 1000)]
    public int BatchSize { get; init; }

    /// <summary>
    /// At most about this many tokens per request (Voyage allows 320,000 for voyage-4). Counts are approximate,
    /// hence the wide margin. On a free Voyage account without a payment method (10,000 tokens per minute),
    /// set it below 10,000 so a single request never exceeds the minute's budget.
    /// </summary>
    [Range(1_000, 320_000)]
    public int MaxTokensPerRequest { get; init; } = 120_000;

    /// <summary>
    /// Retries after a rate limit (429), timeout or server error, with exponentially growing, randomized waits
    /// (2, 4, 8, 16, 32 s, …) that together span Voyage's one-minute rate-limit window. 0 turns retries off.
    /// </summary>
    [Range(0, 10)]
    public int MaxRetryAttempts { get; init; } = 6;

    /// <summary>The first retry's wait; each further retry waits about twice as long (at most 60 s).</summary>
    [Range(0.001, 60)]
    public double RetryBaseDelaySeconds { get; init; } = 2;

    [Range(0, 100)]
    public decimal PricePerMillionTokensUsd { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Vectors are stored in a vector(1024) column: any other size could not be saved, so refuse at startup.
        if (EmbeddingDimensions != Chunk.EmbeddingDimensions)
        {
            yield return new ValidationResult(
                $"EmbeddingDimensions must be {Chunk.EmbeddingDimensions} to match the database column (got {EmbeddingDimensions}). "
                + "Changing it needs a migration and re-embedding every document.",
                [nameof(EmbeddingDimensions)]);
        }
    }
}
