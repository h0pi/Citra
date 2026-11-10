using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Evaluation;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ValidationException = Citra.Application.Common.Exceptions.ValidationException;

namespace Citra.Application.Features.Evaluation;

/// <summary>Adds a test question to an eval set (owners and admins).</summary>
public sealed record AddEvalQuestionCommand(Guid EvalSetId, string Question, string ExpectedAnswer, Guid? ExpectedDocumentId)
    : IRequest<EvalQuestionDto>;

/// <summary>Changes a test question of an eval set (owners and admins).</summary>
public sealed record UpdateEvalQuestionCommand(
    Guid EvalSetId, Guid QuestionId, string Question, string ExpectedAnswer, Guid? ExpectedDocumentId)
    : IRequest<EvalQuestionDto>;

/// <summary>Removes a test question from an eval set (owners and admins).</summary>
public sealed record DeleteEvalQuestionCommand(Guid EvalSetId, Guid QuestionId) : IRequest;

internal sealed class AddEvalQuestionCommandValidator : AbstractValidator<AddEvalQuestionCommand>
{
    public AddEvalQuestionCommandValidator()
    {
        RuleFor(x => x.Question).NotEmpty().MaximumLength(EvalQuestion.QuestionMaxLength);
        RuleFor(x => x.ExpectedAnswer).NotEmpty().MaximumLength(EvalQuestion.ExpectedAnswerMaxLength);
    }
}

internal sealed class UpdateEvalQuestionCommandValidator : AbstractValidator<UpdateEvalQuestionCommand>
{
    public UpdateEvalQuestionCommandValidator()
    {
        RuleFor(x => x.Question).NotEmpty().MaximumLength(EvalQuestion.QuestionMaxLength);
        RuleFor(x => x.ExpectedAnswer).NotEmpty().MaximumLength(EvalQuestion.ExpectedAnswerMaxLength);
    }
}

internal sealed class EvalQuestionCommandHandlers(IApplicationDbContext db, ICurrentUser currentUser) :
    IRequestHandler<AddEvalQuestionCommand, EvalQuestionDto>,
    IRequestHandler<UpdateEvalQuestionCommand, EvalQuestionDto>,
    IRequestHandler<DeleteEvalQuestionCommand>
{
    public async Task<EvalQuestionDto> Handle(AddEvalQuestionCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireEditorTenantId();
        var set = await db.FindEvalSetAsync(request.EvalSetId, cancellationToken);
        await EnsureBotDocumentAsync(set, request.ExpectedDocumentId, cancellationToken);

        var question = set.AddQuestion(request.Question, request.ExpectedAnswer, request.ExpectedDocumentId);
        await db.SaveChangesAsync(cancellationToken);
        return EvalSets.ToDto(question);
    }

    public async Task<EvalQuestionDto> Handle(UpdateEvalQuestionCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireEditorTenantId();
        var set = await db.FindEvalSetAsync(request.EvalSetId, cancellationToken);
        var question = FindQuestion(set, request.QuestionId);
        await EnsureBotDocumentAsync(set, request.ExpectedDocumentId, cancellationToken);

        question.Update(request.Question, request.ExpectedAnswer, request.ExpectedDocumentId);
        await db.SaveChangesAsync(cancellationToken);
        return EvalSets.ToDto(question);
    }

    public async Task Handle(DeleteEvalQuestionCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireEditorTenantId();
        var set = await db.FindEvalSetAsync(request.EvalSetId, cancellationToken);

        db.EvalQuestions.Remove(FindQuestion(set, request.QuestionId));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static EvalQuestion FindQuestion(EvalSet set, Guid questionId) =>
        set.Questions.SingleOrDefault(q => q.Id == questionId) ?? throw new NotFoundException(nameof(EvalQuestion), questionId);

    /// <summary>The expected document must be one of the set's bot's documents.</summary>
    private async Task EnsureBotDocumentAsync(EvalSet set, Guid? documentId, CancellationToken cancellationToken)
    {
        if (documentId is { } id && !await db.Documents.AnyAsync(d => d.Id == id && d.BotId == set.BotId, cancellationToken))
        {
            throw new ValidationException("ExpectedDocumentId", "Choose one of this bot's documents.");
        }
    }
}
