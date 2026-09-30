using Citra.Application.Common.Retrieval;
using FluentValidation;
using MediatR;

namespace Citra.Application.Features.Bots.Search;

/// <summary>
/// Searches a bot's documents the way the chat will, and shows every score: for admins to check and tune what the
/// bot finds (the "Search playground"). The question is embedded once, so it costs a few tokens (recorded).
/// </summary>
public sealed record SearchBotQuery(Guid BotId, string Query, int TopK = SearchBotQuery.DefaultTopK)
    : IRequest<BotSearchResult>
{
    public const int DefaultTopK = 8;
    public const int QueryMaxLength = 1000;
}

/// <summary>What a search found, and the numbers behind it.</summary>
/// <param name="Query">The question as searched (trimmed).</param>
/// <param name="QueryTokens">Tokens the question's embedding cost.</param>
/// <param name="MinSimilarity">Chunks found only by meaning, less similar than this, were dropped.</param>
/// <param name="MinKeywordSimilarity">The same for chunks containing the question's words (see <see cref="IRetrievalSettings"/>).</param>
public sealed record BotSearchResult(
    string Query,
    int QueryTokens,
    double MinSimilarity,
    double MinKeywordSimilarity,
    IReadOnlyList<SearchResultDto> Results);

/// <summary>One passage found: where it is, what it says and how it scored.</summary>
/// <param name="Rank">1-based place in the combined ranking.</param>
/// <param name="ChunkIndex">Position of the (first) chunk in its document: 0, 1, 2, …</param>
/// <param name="Score">Combined score (Reciprocal Rank Fusion of the two searches).</param>
/// <param name="Similarity">Cosine similarity of meaning to the question.</param>
/// <param name="KeywordScore">Full-text rank, if the question's words were found.</param>
/// <param name="VectorRank">Place in the meaning search, if found there.</param>
/// <param name="KeywordRank">Place in the keyword search, if found there.</param>
/// <param name="ChunkCount">Overlapping neighbour chunks joined into this passage.</param>
public sealed record SearchResultDto(
    int Rank,
    Guid ChunkId,
    Guid DocumentId,
    string DocumentName,
    int ChunkIndex,
    int? PageNumber,
    string? Heading,
    string Content,
    int CharStart,
    int CharEnd,
    double Score,
    double? Similarity,
    double? KeywordScore,
    int? VectorRank,
    int? KeywordRank,
    int ChunkCount);

internal sealed class SearchBotQueryValidator : AbstractValidator<SearchBotQuery>
{
    public SearchBotQueryValidator()
    {
        RuleFor(x => x.BotId).NotEmpty();
        RuleFor(x => x.Query).NotEmpty().MaximumLength(SearchBotQuery.QueryMaxLength);
        RuleFor(x => x.TopK).InclusiveBetween(1, IRetriever.MaxTopK);
    }
}
