using Citra.Application.Common.Chunking;
using Microsoft.ML.Tokenizers;

namespace Citra.Infrastructure.Chunking;

/// <summary>
/// Counts tokens with OpenAI's cl100k_base encoding (Microsoft.ML.Tokenizers). Neither Voyage nor Claude
/// publishes its tokenizer for .NET, so this is an approximation; cl100k counts non-English text a little
/// higher than newer tokenizers, which errs on the safe side (slightly smaller chunks, never oversized ones).
/// Thread-safe; created once (loading the vocabulary takes a moment) and shared.
/// </summary>
internal sealed class TiktokenTokenCounter : ITokenCounter
{
    public const string EncodingName = "cl100k_base";

    // No normalizer: indexes returned by the tokenizer refer to the original text, which chunk offsets rely on.
    private readonly Tokenizer _tokenizer = TiktokenTokenizer.CreateForEncoding(EncodingName);

    public int Count(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length == 0 ? 0 : _tokenizer.CountTokens(text);
    }

    public int IndexAfterTokens(string text, int maxTokens)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTokens);
        if (text.Length == 0 || maxTokens == 0)
        {
            return 0;
        }

        return _tokenizer.GetIndexByTokenCount(text, maxTokens, out _, out _);
    }

    public int IndexBeforeLastTokens(string text, int maxTokens)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTokens);
        if (text.Length == 0)
        {
            return 0;
        }

        if (maxTokens == 0)
        {
            return text.Length;
        }

        return _tokenizer.GetIndexByTokenCountFromEnd(text, maxTokens, out _, out _);
    }
}
