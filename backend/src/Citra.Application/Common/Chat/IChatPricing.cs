namespace Citra.Application.Common.Chat;

/// <summary>What a model's answer cost, from the configured price list (section "Chat:Prices").</summary>
public interface IChatPricing
{
    /// <summary>Dollars for <paramref name="usage"/>; 0 for a model without a price (logged, so it gets one).</summary>
    decimal Cost(ChatUsage usage);
}

/// <summary>A model's price in US dollars per million tokens read (input) and written (output).</summary>
public sealed record ModelPrice(decimal InputPerMillionUsd, decimal OutputPerMillionUsd)
{
    /// <summary>Exact decimal math: one short answer costs fractions of a cent, which must not round away.</summary>
    public decimal Cost(int inputTokens, int outputTokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(outputTokens);
        return ((inputTokens * InputPerMillionUsd) + (outputTokens * OutputPerMillionUsd)) / 1_000_000m;
    }
}
