using System.ComponentModel.DataAnnotations;
using Citra.Application.Features.Bots;

namespace Citra.Infrastructure.Options;

/// <summary>Which model writes the answers, and how much of a conversation it sees. Section: "Chat".</summary>
public sealed class ChatOptions : IValidatableObject
{
    public const string SectionName = "Chat";

    /// <summary>
    /// <see cref="ChatProvider.Extractive"/> (free, no AI: quotes the best sentences of the sources) until the
    /// Anthropic account has credits; then <see cref="ChatProvider.Anthropic"/> (Claude, settings in "Anthropic").
    /// </summary>
    [EnumDataType(typeof(ChatProvider))]
    public ChatProvider Provider { get; init; } = ChatProvider.Extractive;

    /// <summary>Earlier messages sent with a new question: enough for follow-ups, cheap enough for long chats.</summary>
    [Range(0, 100)]
    public int MaxHistoryMessages { get; init; } = 10;

    /// <summary>Token budget for those earlier messages (answers can be long).</summary>
    [Range(0, 100_000)]
    public int MaxHistoryTokens { get; init; } = 3000;

    /// <summary>
    /// Passages the model gets per answer: enough to cover a question from several places, few enough that the
    /// prompt stays cheap and focused (about 500 tokens each).
    /// </summary>
    [Range(1, 50)]
    public int SourcesPerAnswer { get; init; } = 6;

    /// <summary>
    /// US dollars per million tokens, per model, for the cost of each answer. Check Anthropic's price list when
    /// adding a model; override one with e.g. Chat:Prices:claude-sonnet-5-5:InputPerMillionUsd. A model Claude
    /// reports with a date ("claude-sonnet-5-5-20260901") uses the price of its name without the date.
    /// </summary>
    public Dictionary<string, ModelPriceOptions> Prices { get; init; } = new(StringComparer.Ordinal)
    {
        ["claude-opus-5-5"] = new() { InputPerMillionUsd = 5m, OutputPerMillionUsd = 25m },
        ["claude-sonnet-5-5"] = new() { InputPerMillionUsd = 3m, OutputPerMillionUsd = 15m },
        ["claude-haiku-4-5"] = new() { InputPerMillionUsd = 1m, OutputPerMillionUsd = 5m },
        ["citra-extractive"] = new() { InputPerMillionUsd = 0m, OutputPerMillionUsd = 0m },
    };

    /// <summary>Rewrites follow-up questions: a small task, so the cheapest, fastest model.</summary>
    [Required]
    public string RewriteModel { get; init; } = "claude-haiku-4-5";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!BotModels.IsSupported(RewriteModel ?? string.Empty))
        {
            yield return new ValidationResult(
                $"RewriteModel must be one of: {string.Join(", ", BotModels.Supported)}.", [nameof(RewriteModel)]);
        }

        // Every model a bot can choose must have a price, or its answers would be recorded as free.
        foreach (var model in BotModels.Supported.Where(m => !Prices.ContainsKey(m)))
        {
            yield return new ValidationResult($"Prices has no price for {model}.", [nameof(Prices)]);
        }

        foreach (var (model, price) in Prices.Where(p => p.Value.InputPerMillionUsd < 0 || p.Value.OutputPerMillionUsd < 0))
        {
            yield return new ValidationResult($"The price of {model} cannot be negative.", [nameof(Prices)]);
        }
    }
}

/// <summary>A model's price in US dollars per million tokens (Chat:Prices:&lt;model&gt;).</summary>
public sealed class ModelPriceOptions
{
    public decimal InputPerMillionUsd { get; init; }

    public decimal OutputPerMillionUsd { get; init; }
}

public enum ChatProvider
{
    /// <summary>The free stand-in: answers by quoting the sources, costs nothing, needs no key.</summary>
    Extractive,

    /// <summary>Claude through the Anthropic Messages API (needs Anthropic:ApiKey and credits).</summary>
    Anthropic,
}
