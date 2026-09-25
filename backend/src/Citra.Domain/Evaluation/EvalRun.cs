using Citra.Domain.Common;

namespace Citra.Domain.Evaluation;

/// <summary>
/// One execution of an eval set against a bot. Stores the model used, so runs before and after a change
/// (prompt, model, chunking) can be compared. Scores are computed from the per-question results.
/// </summary>
public sealed class EvalRun : Entity, ITenantOwned
{
    public const int ModelMaxLength = 100;
    public const int NotesMaxLength = 500;
    public const int ErrorMaxLength = 2000;

    private readonly List<EvalResult> _results = [];

    public Guid TenantId { get; private set; }

    public Guid EvalSetId { get; private set; }

    public Guid BotId { get; private set; }

    public EvalRunStatus Status { get; private set; } = EvalRunStatus.Pending;

    /// <summary>Chat model used for this run (snapshot, since the bot's setting can change later).</summary>
    public string Model { get; private set; } = string.Empty;

    /// <summary>Optional label, e.g. "after prompt v2".</summary>
    public string? Notes { get; private set; }

    public int TotalQuestions { get; private set; }

    public int CorrectCount { get; private set; }

    public int PartialCount { get; private set; }

    public int RetrievalHitCount { get; private set; }

    /// <summary>0–100: correct = 1 point, partial = 0.5, divided by total questions.</summary>
    public double? Score { get; private set; }

    public decimal TotalCostUsd { get; private set; }

    public string? Error { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public IReadOnlyList<EvalResult> Results => _results;

    private EvalRun()
    {
        // For EF Core.
    }

    public static EvalRun Create(Guid tenantId, Guid evalSetId, Guid botId, string model, int totalQuestions, string? notes = null)
    {
        if (tenantId == Guid.Empty || evalSetId == Guid.Empty || botId == Guid.Empty)
        {
            throw new ArgumentException("Tenant, eval set and bot ids are required.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalQuestions);

        return new EvalRun
        {
            TenantId = tenantId,
            EvalSetId = evalSetId,
            BotId = botId,
            Model = model,
            TotalQuestions = totalQuestions,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };
    }

    public void Start(DateTimeOffset now)
    {
        if (Status != EvalRunStatus.Pending)
        {
            throw new InvalidOperationException($"Eval run {Id} has already started.");
        }

        Status = EvalRunStatus.Running;
        StartedAt = now;
    }

    public EvalResult AddResult(
        Guid evalQuestionId,
        string actualAnswer,
        EvalGrade grade,
        string judgeReason,
        bool retrievalHit,
        int latencyMs,
        decimal costUsd)
    {
        if (Status != EvalRunStatus.Running)
        {
            throw new InvalidOperationException($"Eval run {Id} is not running.");
        }

        var result = EvalResult.Create(Id, evalQuestionId, actualAnswer, grade, judgeReason, retrievalHit, latencyMs, costUsd);
        _results.Add(result);
        return result;
    }

    public void Complete(DateTimeOffset now)
    {
        if (Status != EvalRunStatus.Running)
        {
            throw new InvalidOperationException($"Eval run {Id} is not running.");
        }

        CorrectCount = _results.Count(r => r.Grade == EvalGrade.Correct);
        PartialCount = _results.Count(r => r.Grade == EvalGrade.Partial);
        RetrievalHitCount = _results.Count(r => r.RetrievalHit);
        TotalCostUsd = _results.Sum(r => r.CostUsd);
        Score = Math.Round((CorrectCount + (PartialCount * 0.5)) / TotalQuestions * 100, 1);
        Status = EvalRunStatus.Completed;
        CompletedAt = now;
    }

    public void Fail(string error, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        Status = EvalRunStatus.Failed;
        Error = error.Length > ErrorMaxLength ? error[..ErrorMaxLength] : error;
        CompletedAt = now;
    }
}
