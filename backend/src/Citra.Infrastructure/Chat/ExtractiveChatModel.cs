using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Citra.Application.Common.Chat;
using Citra.Application.Common.Chunking;

namespace Citra.Infrastructure.Chat;

/// <summary>
/// The free stand-in for Claude, used until the Anthropic account has credits (Chat:Provider = Extractive). It is
/// not a language model: it reads the numbered sources in the prompt and answers by quoting the sentences that
/// share the most words with the question, each cited like Claude would ([1], [2]). With no sources it answers
/// "not found" (<see cref="ChatPrompts.NotFoundMarker"/>); a prompt without sources (e.g. rewriting a follow-up
/// question) gets the question back unchanged. So the whole chat works end to end, for $0.
/// </summary>
internal sealed partial class ExtractiveChatModel(ITokenCounter tokens) : IChatModel
{
    public const string ModelName = "citra-extractive";

    /// <summary>At most this many sentences are quoted.</summary>
    internal const int MaxSentences = 2;

    /// <summary>Lines with more words are never titles.</summary>
    internal const int MaxTitleWords = 5;

    internal const string NotFoundAnswer = ChatPrompts.NotFoundMarker + " I could not find this in the documents.";

    private static readonly HashSet<string> CommonWords =
    [
        "the", "and", "for", "are", "was", "were", "what", "when", "where", "which", "who", "why", "how", "does",
        "did", "can", "could", "should", "would", "will", "with", "from", "this", "that", "these", "those", "there",
        "have", "has", "had", "you", "your", "our", "their", "about", "into", "any", "all", "get", "much", "many",
    ];

    public string DefaultModel => ModelName;

    public async IAsyncEnumerable<ChatEvent> StreamAsync(
        ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();

        var answer = Answer(request.Messages[^1].Content);

        // Word by word, like a real model streams.
        foreach (Match word in WordWithSpace().Matches(answer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new ChatTextDelta(word.Value);
        }

        var input = tokens.Count(request.SystemPrompt) + request.Messages.Sum(m => tokens.Count(m.Content));
        yield return new ChatCompleted(new ChatUsage(ModelName, input, tokens.Count(answer)), "end_turn");
    }

    internal static string Answer(string userTurn)
    {
        var question = QuestionTag().Match(userTurn) is { Success: true } q
            ? SourceList.Unescape(q.Groups["text"].Value.Trim())
            : userTurn.Trim();

        // No sources block: not a question about documents (e.g. "rewrite this question"): answer with the question.
        if (!userTurn.Contains("<sources>", StringComparison.Ordinal))
        {
            return question;
        }

        var sources = SourceTag().Matches(userTurn)
            .Select(m => (Number: int.Parse(m.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture),
                Sentences: Sentences(SourceList.Unescape(m.Groups["text"].Value))))
            .Where(s => s.Sentences.Count > 0)
            .ToList();
        if (sources.Count == 0)
        {
            return NotFoundAnswer;
        }

        var wanted = Words(question);
        var candidates = sources
            .SelectMany(s => s.Sentences.Select((sentence, position) =>
                (s.Number, Sentence: sentence, Overlap: Words(sentence).Count(wanted.Contains), Position: position)))
            .ToList();

        // A second sentence must be about as relevant as the best one (half its shared words), not just mention one.
        var best = candidates.Count > 0 ? candidates.Max(q => q.Overlap) : 0;
        var quotes = candidates
            .Where(q => q.Overlap > 0 && q.Overlap * 2 >= best)
            .OrderByDescending(q => q.Overlap)
            .ThenBy(q => q.Number)
            .ThenBy(q => q.Position)
            .Take(MaxSentences)
            .OrderBy(q => q.Number)
            .ThenBy(q => q.Position)
            .Select(q => (q.Number, q.Sentence))
            .ToList();

        // No shared words (e.g. a question in another language): the search still judged source 1 relevant.
        if (quotes.Count == 0)
        {
            quotes = [(sources[0].Number, sources[0].Sentences[0])];
        }

        return string.Join(" ", quotes.Select(q => Cite(q.Sentence, q.Number)));
    }

    /// <summary>"Employees get 25 days." + 1 → "Employees get 25 days [1]."</summary>
    private static string Cite(string sentence, int number) =>
        ".!?".Contains(sentence[^1])
            ? $"{sentence[..^1].TrimEnd()} [{number}]{sentence[^1]}"
            : $"{sentence} [{number}].";

    /// <summary>
    /// The text's sentences. Lines are first joined into paragraphs, because PDF text breaks lines in the middle
    /// of sentences; titles are left out (they are not statements): Markdown headings, and short lines like
    /// "Sick leave" that start a paragraph without ending in punctuation.
    /// </summary>
    internal static List<string> Sentences(string text)
    {
        var paragraphs = new List<string>();
        var lines = new List<string>();
        foreach (var line in text.Split('\n').Select(l => l.Trim()))
        {
            var startsParagraph = lines.Count == 0 || ".!?:".Contains(lines[^1][^1]);
            if (line.Length == 0 || line.StartsWith('#') || (startsParagraph && IsTitle(line)))
            {
                EndParagraph();
                continue;
            }

            lines.Add(line);
        }

        EndParagraph();
        return [.. paragraphs
            .SelectMany(paragraph => SentenceEnd().Split(paragraph))
            .Select(sentence => sentence.Trim())
            .Where(sentence => sentence.Length > 0)];

        void EndParagraph()
        {
            if (lines.Count > 0)
            {
                paragraphs.Add(string.Join(' ', lines));
                lines.Clear();
            }
        }
    }

    /// <summary>A few capitalized words without closing punctuation: "Vacation", "Payment terminal errors".</summary>
    private static bool IsTitle(string line) =>
        char.IsUpper(line[0])
        && !".!?:;,".Contains(line[^1])
        && line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= MaxTitleWords;

    /// <summary>Lowercase words of 3+ letters, without common question words, plural "s" removed.</summary>
    private static HashSet<string> Words(string text) =>
        [.. Word().Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(w => w.Length >= 3 && !CommonWords.Contains(w))
            .Select(w => w.Length > 3 && w.EndsWith('s') ? w[..^1] : w)];

    [GeneratedRegex(@"<question>(?<text>.*?)</question>", RegexOptions.Singleline)]
    private static partial Regex QuestionTag();

    [GeneratedRegex(@"<source id=""(?<id>\d+)""[^>]*>(?<text>.*?)</source>", RegexOptions.Singleline)]
    private static partial Regex SourceTag();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:-[\p{L}\p{N}]+)*")]
    private static partial Regex Word();

    [GeneratedRegex(@"\S+\s*")]
    private static partial Regex WordWithSpace();
}
