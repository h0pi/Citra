namespace Citra.Domain.Evaluation;

/// <summary>How well an answer matched the expected answer (decided by an LLM judge).</summary>
public enum EvalGrade
{
    Wrong = 0,
    Partial = 1,
    Correct = 2,
}
