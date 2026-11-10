using Citra.Domain.Common;

namespace Citra.Domain.Evaluation;

/// <summary>The outcome of one test question in an eval run.</summary>
public sealed class EvalResult : Entity
{
    public const int ActualAnswerMaxLength = 8000;
    public const int JudgeReasonMaxLength = 2000;

    public Guid EvalRunId { get; private set; }

    public Guid EvalQuestionId { get; private set; }

    public string ActualAnswer { get; private set; } = string.Empty;

    public EvalGrade Grade { get; private set; }

    /// <summary>The judge's short explanation of the grade.</summary>
    public string JudgeReason { get; private set; } = string.Empty;

    /// <summary>Whether the expected document was among the retrieved sources.</summary>
    public bool RetrievalHit { get; private set; }

    public int LatencyMs { get; private set; }

    public decimal CostUsd { get; private set; }

    private EvalResult()
    {
        // For EF Core.
    }

    internal static EvalResult Create(
        Guid evalRunId,
        Guid evalQuestionId,
        string actualAnswer,
        EvalGrade grade,
        string judgeReason,
        bool retrievalHit,
        int latencyMs,
        decimal costUsd)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(latencyMs);
        ArgumentOutOfRangeException.ThrowIfNegative(costUsd);

        return new EvalResult
        {
            EvalRunId = evalRunId,
            EvalQuestionId = evalQuestionId,
            ActualAnswer = Truncate(actualAnswer ?? string.Empty, ActualAnswerMaxLength),
            Grade = grade,
            JudgeReason = Truncate(judgeReason ?? string.Empty, JudgeReasonMaxLength),
            RetrievalHit = retrievalHit,
            LatencyMs = latencyMs,
            CostUsd = Math.Round(costUsd, 6), // as stored, so the run's total equals the sum of its results
        };
    }

    private static string Truncate(string value, int max) => value.Length > max ? value[..max] : value;
}
