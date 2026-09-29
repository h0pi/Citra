namespace Citra.Application.Common.Embeddings;

/// <summary>Turns billed tokens into dollars. Exact decimal math: small amounts must not round away.</summary>
public static class EmbeddingCost
{
    /// <summary>
    /// Cost of <paramref name="tokens"/> at <paramref name="pricePerMillionTokensUsd"/>. A 4-token question at
    /// $0.06 per million costs $0.00000024: stored as is, so thousands of questions still add up correctly.
    /// </summary>
    public static decimal Calculate(int tokens, decimal pricePerMillionTokensUsd)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tokens);
        ArgumentOutOfRangeException.ThrowIfNegative(pricePerMillionTokensUsd);
        return tokens * pricePerMillionTokensUsd / 1_000_000m;
    }
}
