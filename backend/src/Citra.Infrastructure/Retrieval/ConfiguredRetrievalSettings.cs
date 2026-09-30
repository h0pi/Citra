using Citra.Application.Common.Retrieval;
using Citra.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Citra.Infrastructure.Retrieval;

internal sealed class ConfiguredRetrievalSettings(IOptions<RetrievalOptions> options) : IRetrievalSettings
{
    public double MinSimilarity { get; } = options.Value.MinSimilarity;

    public double MinKeywordSimilarity { get; } = options.Value.MinKeywordSimilarity;
}
