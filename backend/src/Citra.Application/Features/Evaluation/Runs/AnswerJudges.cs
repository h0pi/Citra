using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Citra.Application.Common.Chat;
using Citra.Domain.Evaluation;

namespace Citra.Application.Features.Evaluation.Runs;

/// <summary>Grades a bot's answer against the expected one: correct, partly correct or wrong, with a short reason.</summary>
public interface IAnswerJudge
{
    Task<JudgeVerdict> GradeAsync(string question, string expectedAnswer, string actualAnswer, CancellationToken cancellationToken);
}

/// <param name="CostUsd">What grading cost (0 for the free stand-in).</param>
public sealed record JudgeVerdict(EvalGrade Grade, string Reason, decimal CostUsd);

/// <summary>
/// The free stand-in judge (no AI): the share of the expected answer's words that also appear in the bot's answer.
/// At least 70% is correct, at least 40% partly correct. Rough — wording differences count against an answer — but
/// it costs nothing and runs offline.
/// </summary>
public sealed partial class WordOverlapJudge : IAnswerJudge
{
    public const double CorrectShare = 0.7;
    public const double PartialShare = 0.4;

    public Task<JudgeVerdict> GradeAsync(string question, string expectedAnswer, string actualAnswer, CancellationToken cancellationToken)
    {
        var expected = Words(expectedAnswer);
        var actual = Words(actualAnswer);
        var share = expected.Count == 0 ? 0 : (double)expected.Count(actual.Contains) / expected.Count;
        var grade = share >= CorrectShare ? EvalGrade.Correct : share >= PartialShare ? EvalGrade.Partial : EvalGrade.Wrong;

        return Task.FromResult(new JudgeVerdict(
            grade, $"{Math.Round(share * 100)}% of the expected answer's words are in the answer (word comparison, no AI).", 0));
    }

    /// <summary>Distinct lower-case words of any language; single letters and the like are ignored.</summary>
    private static HashSet<string> Words(string text) =>
        [.. Word().Matches(text.ToLowerInvariant()).Select(m => m.Value).Where(w => w.Length > 1 || char.IsDigit(w[0]))];

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();
}

/// <summary>
/// An AI judge (the chat model, e.g. Claude): it compares the answers and replies with a grade and a reason in JSON.
/// Used when a real model is configured; a reply it cannot read counts as wrong, with that as the reason.
/// </summary>
public sealed class ModelAnswerJudge(IChatModel model, IChatPricing pricing) : IAnswerJudge
{
    internal const string SystemPrompt = """
        You grade answers of a company's document assistant. Compare the actual answer with the expected answer.
        - "correct": it states the facts of the expected answer (other wording is fine).
        - "partial": some facts are right, others are missing or wrong.
        - "wrong": the facts are wrong or missing, or it says it could not find the answer.
        Reply with JSON only: {"grade": "correct" | "partial" | "wrong", "reason": "<one short sentence>"}.
        """;

    public async Task<JudgeVerdict> GradeAsync(string question, string expectedAnswer, string actualAnswer, CancellationToken cancellationToken)
    {
        var request = new ChatRequest(SystemPrompt, [new ChatMessage(ChatRole.User, $"""
            <question>{question}</question>
            <expected_answer>{expectedAnswer}</expected_answer>
            <actual_answer>{actualAnswer}</actual_answer>
            """)])
        {
            Temperature = 0,
        };

        var reply = new StringBuilder();
        decimal cost = 0;
        await foreach (var chatEvent in model.StreamAsync(request, cancellationToken))
        {
            if (chatEvent is ChatTextDelta delta)
            {
                reply.Append(delta.Text);
            }
            else if (chatEvent is ChatCompleted completed)
            {
                cost = pricing.Cost(completed.Usage);
            }
        }

        return Parse(reply.ToString()) is var (grade, reason)
            ? new JudgeVerdict(grade, reason, cost)
            : new JudgeVerdict(EvalGrade.Wrong, "The judge's reply could not be read.", cost);
    }

    /// <summary>The grade and reason from the judge's JSON (it may be wrapped in other text), or null.</summary>
    internal static (EvalGrade Grade, string Reason)? Parse(string reply)
    {
        var (start, end) = (reply.IndexOf('{'), reply.LastIndexOf('}'));
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(reply[start..(end + 1)]);
            var grade = json.RootElement.GetProperty("grade").GetString()?.Trim().ToLowerInvariant() switch
            {
                "correct" => EvalGrade.Correct,
                "partial" => EvalGrade.Partial,
                "wrong" => EvalGrade.Wrong,
                _ => (EvalGrade?)null,
            };
            var reason = json.RootElement.TryGetProperty("reason", out var r) ? r.GetString()?.Trim() : null;
            return grade is { } g ? (g, string.IsNullOrEmpty(reason) ? "No reason given." : reason) : null;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
