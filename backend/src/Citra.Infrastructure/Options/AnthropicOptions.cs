using System.ComponentModel.DataAnnotations;

namespace Citra.Infrastructure.Options;

/// <summary>Settings for the Claude (Anthropic) chat model. Section: "Anthropic".</summary>
public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    /// <summary>
    /// Not required at startup: only needed when Chat:Provider is Anthropic.
    /// The Claude client throws a clear error if it is called without one.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    [Required, Url]
    public string BaseUrl { get; init; } = "https://api.anthropic.com/";

    /// <summary>The default model (a bot can pick another one of BotModels.Supported).</summary>
    [Required]
    public string ChatModel { get; init; } = string.Empty;

    [Range(256, 128_000)]
    public int MaxOutputTokens { get; init; }

    /// <summary>
    /// Retries when Anthropic is overloaded (529), rate-limits (429), times out or has a server error, before the
    /// answer starts. Once text is streaming, a failure is reported instead (the user already saw part of it).
    /// </summary>
    [Range(0, 10)]
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>The first retry's wait; each further retry waits about twice as long.</summary>
    [Range(0.001, 60)]
    public double RetryBaseDelaySeconds { get; init; } = 2;
}
