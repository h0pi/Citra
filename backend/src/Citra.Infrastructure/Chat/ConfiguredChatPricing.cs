using Citra.Application.Common.Chat;
using Citra.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Citra.Infrastructure.Chat;

/// <summary>Prices from Chat:Prices. Claude may report a dated model id; it is priced by its undated name.</summary>
internal sealed class ConfiguredChatPricing(IOptions<ChatOptions> options, ILogger<ConfiguredChatPricing> logger) : IChatPricing
{
    private readonly IReadOnlyDictionary<string, ModelPrice> _prices = options.Value.Prices.ToDictionary(
        p => p.Key, p => new ModelPrice(p.Value.InputPerMillionUsd, p.Value.OutputPerMillionUsd), StringComparer.Ordinal);

    public decimal Cost(ChatUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        if (PriceOf(usage.Model) is { } price)
        {
            return price.Cost(usage.InputTokens, usage.OutputTokens);
        }

        logger.LogWarning(
            "No price for chat model {Model}: its tokens are recorded but its cost as 0. Add it to Chat:Prices.", usage.Model);
        return 0m;
    }

    /// <summary>The exact name, else the longest configured name it starts with, followed by "-" (a date suffix).</summary>
    internal ModelPrice? PriceOf(string model) =>
        _prices.TryGetValue(model, out var exact)
            ? exact
            : _prices.Where(p => model.StartsWith(p.Key + "-", StringComparison.Ordinal))
                .OrderByDescending(p => p.Key.Length)
                .Select(p => p.Value)
                .FirstOrDefault();
}
