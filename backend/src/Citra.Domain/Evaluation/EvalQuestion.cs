using Citra.Domain.Common;

namespace Citra.Domain.Evaluation;

/// <summary>One test question, the answer a correct bot should give, and (optionally) the document it should come from.</summary>
public sealed class EvalQuestion : Entity
{
    public const int QuestionMaxLength = 1000;
    public const int ExpectedAnswerMaxLength = 4000;

    public Guid EvalSetId { get; private set; }

    public string Question { get; private set; } = string.Empty;

    public string ExpectedAnswer { get; private set; } = string.Empty;

    /// <summary>If set, retrieval is scored on whether this document appears among the top sources.</summary>
    public Guid? ExpectedDocumentId { get; private set; }

    private EvalQuestion()
    {
        // For EF Core.
    }

    internal static EvalQuestion Create(Guid evalSetId, string question, string expectedAnswer, Guid? expectedDocumentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAnswer);

        return new EvalQuestion
        {
            EvalSetId = evalSetId,
            Question = question.Trim(),
            ExpectedAnswer = expectedAnswer.Trim(),
            ExpectedDocumentId = expectedDocumentId,
        };
    }

    public void Update(string question, string expectedAnswer, Guid? expectedDocumentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAnswer);

        Question = question.Trim();
        ExpectedAnswer = expectedAnswer.Trim();
        ExpectedDocumentId = expectedDocumentId;
    }
}
