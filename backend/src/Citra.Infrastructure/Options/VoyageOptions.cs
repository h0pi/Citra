using System.ComponentModel.DataAnnotations;

namespace Citra.Infrastructure.Options;

/// <summary>Settings for Voyage AI embeddings. Section: "Voyage".</summary>
public sealed class VoyageOptions
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

    [Range(1, 128)]
    public int BatchSize { get; init; }

    [Range(0, 100)]
    public decimal PricePerMillionTokensUsd { get; init; }
}
