using Citra.Domain.Documents;

namespace Citra.Application.Common.Extraction;

/// <summary>Picks the extractor registered for a document type (PDF, Word, text/Markdown, web page).</summary>
public sealed class TextExtractorFactory : ITextExtractorFactory
{
    private readonly Dictionary<DocumentSourceType, ITextExtractor> _bySourceType = [];

    public TextExtractorFactory(IEnumerable<ITextExtractor> extractors)
    {
        foreach (var extractor in extractors)
        {
            foreach (var sourceType in extractor.SourceTypes)
            {
                // Two extractors for one type is a registration mistake: fail at startup, not on some upload.
                if (!_bySourceType.TryAdd(sourceType, extractor))
                {
                    throw new InvalidOperationException(
                        $"Both {_bySourceType[sourceType].GetType().Name} and {extractor.GetType().Name} read {sourceType}.");
                }
            }
        }
    }

    public ITextExtractor For(DocumentSourceType sourceType) =>
        _bySourceType.TryGetValue(sourceType, out var extractor)
            ? extractor
            : throw new InvalidOperationException($"No text extractor is registered for {sourceType} documents.");
}
