namespace Citra.Application.Common.Chunking;

/// <summary>
/// Measures text in tokens, the unit AI models read and bill in, so chunks can be sized for search
/// (~500 tokens each). Counts are approximate: every model family has its own tokenizer, so treat them as
/// sizing guidance, not as an exact bill. All character indexes refer to the original <c>text</c> passed in.
/// </summary>
public interface ITokenCounter
{
    /// <summary>Number of tokens in <paramref name="text"/> (0 for empty text).</summary>
    int Count(string text);

    /// <summary>
    /// End index of the longest start of <paramref name="text"/> with at most <paramref name="maxTokens"/> tokens:
    /// <c>text[..index]</c> fits, and never ends inside a token or a character. Returns <c>text.Length</c> if the
    /// whole text fits, 0 if not even one token does.
    /// </summary>
    int IndexAfterTokens(string text, int maxTokens);

    /// <summary>
    /// Start index of the longest end of <paramref name="text"/> with at most <paramref name="maxTokens"/> tokens:
    /// <c>text[index..]</c> fits. Returns 0 if the whole text fits, <c>text.Length</c> if not even one token does.
    /// </summary>
    int IndexBeforeLastTokens(string text, int maxTokens);
}
