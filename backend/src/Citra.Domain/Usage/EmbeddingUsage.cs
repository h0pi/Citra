using Citra.Domain.Common;

namespace Citra.Domain.Usage;

/// <summary>What an embedding was made for.</summary>
public enum EmbeddingPurpose
{
    /// <summary>A document's chunks, embedded while processing it.</summary>
    Document = 0,

    /// <summary>A user's question, embedded to search the chunks.</summary>
    Query = 1,
}

/// <summary>
/// One embedding call's token usage and cost, for the cost dashboard ("chat vs embeddings, per bot").
/// Kept when its document or bot is deleted (the money was still spent); only the links are cleared.
/// </summary>
public sealed class EmbeddingUsage : Entity, ITenantOwned
{
    public const int ModelMaxLength = 100;

    public Guid TenantId { get; private set; }

    /// <summary>Null once the bot was deleted.</summary>
    public Guid? BotId { get; private set; }

    /// <summary>The embedded document (Purpose = Document); null for questions or once the document was deleted.</summary>
    public Guid? DocumentId { get; private set; }

    public EmbeddingPurpose Purpose { get; private set; }

    /// <summary>Model that produced the embeddings, e.g. "voyage-4" (prices differ per model).</summary>
    public string Model { get; private set; } = string.Empty;

    /// <summary>Tokens the provider billed.</summary>
    public int Tokens { get; private set; }

    /// <summary>Cost at the price that applied at the time (prices can change later).</summary>
    public decimal CostUsd { get; private set; }

    private EmbeddingUsage()
    {
        // For EF Core.
    }

    public static EmbeddingUsage ForDocument(
        Guid tenantId, Guid botId, Guid documentId, string model, int tokens, decimal costUsd)
    {
        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("Document id is required.", nameof(documentId));
        }

        return Create(tenantId, botId, documentId, EmbeddingPurpose.Document, model, tokens, costUsd);
    }

    public static EmbeddingUsage ForQuery(Guid tenantId, Guid botId, string model, int tokens, decimal costUsd) =>
        Create(tenantId, botId, documentId: null, EmbeddingPurpose.Query, model, tokens, costUsd);

    private static EmbeddingUsage Create(
        Guid tenantId, Guid botId, Guid? documentId, EmbeddingPurpose purpose, string model, int tokens, decimal costUsd)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (botId == Guid.Empty)
        {
            throw new ArgumentException("Bot id is required.", nameof(botId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (model.Trim().Length > ModelMaxLength)
        {
            throw new ArgumentException($"Model name is longer than {ModelMaxLength} characters.", nameof(model));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(tokens);
        ArgumentOutOfRangeException.ThrowIfNegative(costUsd);

        return new EmbeddingUsage
        {
            TenantId = tenantId,
            BotId = botId,
            DocumentId = documentId,
            Purpose = purpose,
            Model = model.Trim(),
            Tokens = tokens,
            CostUsd = costUsd,
        };
    }
}
