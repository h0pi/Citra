using Citra.Domain.Common;

namespace Citra.Domain.Evaluation;

/// <summary>A named set of test questions with expected answers, used to measure a bot's accuracy.</summary>
public sealed class EvalSet : Entity, ITenantOwned
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    private readonly List<EvalQuestion> _questions = [];

    public Guid TenantId { get; private set; }

    public Guid BotId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public IReadOnlyList<EvalQuestion> Questions => _questions;

    private EvalSet()
    {
        // For EF Core.
    }

    public static EvalSet Create(Guid tenantId, Guid botId, string name, string? description = null)
    {
        if (tenantId == Guid.Empty || botId == Guid.Empty)
        {
            throw new ArgumentException("Tenant and bot ids are required.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new EvalSet
        {
            TenantId = tenantId,
            BotId = botId,
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
        };
    }

    public void Rename(string name, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public EvalQuestion AddQuestion(string question, string expectedAnswer, Guid? expectedDocumentId = null)
    {
        var position = _questions.Count == 0 ? 0 : _questions.Max(q => q.Position) + 1;
        var item = EvalQuestion.Create(Id, position, question, expectedAnswer, expectedDocumentId);
        _questions.Add(item);
        return item;
    }

    public void RemoveQuestion(Guid questionId) => _questions.RemoveAll(q => q.Id == questionId);
}
