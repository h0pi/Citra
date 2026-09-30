using Citra.Application.Common.Embeddings;
using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Retrieval;
using Citra.Application.Common.Security;
using Citra.Domain.Bots;
using Citra.Domain.Usage;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Bots.Search;

internal sealed class SearchBotQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IRetriever retriever,
    IEmbeddingService embeddings,
    IRetrievalSettings settings) : IRequestHandler<SearchBotQuery, BotSearchResult>
{
    public async Task<BotSearchResult> Handle(SearchBotQuery request, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.RequireAdminTenantId();

        // The retriever trusts the bot id it gets: the tenant filter makes another company's bot a 404 here.
        if (!await db.Bots.AnyAsync(b => b.Id == request.BotId, cancellationToken))
        {
            throw new NotFoundException(nameof(Bot), request.BotId);
        }

        var result = await retriever.SearchAsync(request.BotId, request.Query, request.TopK, cancellationToken);

        // The provider billed the question's tokens: record them for the cost dashboard.
        db.EmbeddingUsages.Add(EmbeddingUsage.ForQuery(
            tenantId, request.BotId, embeddings.Model, result.QueryTokens,
            EmbeddingCost.Calculate(result.QueryTokens, embeddings.PricePerMillionTokensUsd)));
        await db.SaveChangesAsync(cancellationToken);

        return new BotSearchResult(
            request.Query.Trim(),
            result.QueryTokens,
            settings.MinSimilarity,
            settings.MinKeywordSimilarity,
            [.. result.Chunks.Select((c, i) => new SearchResultDto(
                i + 1, c.ChunkId, c.DocumentId, c.DocumentName, c.Index, c.PageNumber, c.Heading, c.Content,
                c.CharStart, c.CharEnd, c.Score, c.Similarity, c.KeywordScore, c.VectorRank, c.KeywordRank,
                c.ChunkCount))]);
    }
}
