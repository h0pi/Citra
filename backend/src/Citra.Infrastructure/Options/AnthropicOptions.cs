using System.ComponentModel.DataAnnotations;

namespace Citra.Infrastructure.Options;

/// <summary>Settings for the Claude (Anthropic) chat model. Section: "Anthropic".</summary>
public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    /// <summary>
    /// Not required at startup: the key is only needed once chat is implemented and used.
    /// The Claude client throws a clear error if it is called without one.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string ChatModel { get; init; } = string.Empty;

    [Range(256, 128_000)]
    public int MaxOutputTokens { get; init; }

    [Range(0, 1_000)]
    public decimal InputPricePerMillionTokensUsd { get; init; }

    [Range(0, 1_000)]
    public decimal OutputPricePerMillionTokensUsd { get; init; }
}
