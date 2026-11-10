using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Bots;
using Citra.Domain.Evaluation;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Evaluation;

/// <summary>A new, empty eval set for a bot (owners and admins).</summary>
public sealed record CreateEvalSetCommand(Guid BotId, string Name, string? Description) : IRequest<EvalSetDto>;

/// <summary>Renames an eval set and changes its description (owners and admins).</summary>
public sealed record UpdateEvalSetCommand(Guid Id, string Name, string? Description) : IRequest<EvalSetDto>;

/// <summary>Deletes an eval set with its questions and runs (owners and admins).</summary>
public sealed record DeleteEvalSetCommand(Guid Id) : IRequest;

internal sealed class CreateEvalSetCommandValidator : AbstractValidator<CreateEvalSetCommand>
{
    public CreateEvalSetCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(EvalSet.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(EvalSet.DescriptionMaxLength);
    }
}

internal sealed class UpdateEvalSetCommandValidator : AbstractValidator<UpdateEvalSetCommand>
{
    public UpdateEvalSetCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(EvalSet.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(EvalSet.DescriptionMaxLength);
    }
}

internal sealed class EvalSetCommandHandlers(IApplicationDbContext db, ICurrentUser currentUser) :
    IRequestHandler<CreateEvalSetCommand, EvalSetDto>,
    IRequestHandler<UpdateEvalSetCommand, EvalSetDto>,
    IRequestHandler<DeleteEvalSetCommand>
{
    public async Task<EvalSetDto> Handle(CreateEvalSetCommand request, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.RequireEditorTenantId();
        if (!await db.Bots.AnyAsync(b => b.Id == request.BotId, cancellationToken))
        {
            throw new NotFoundException(nameof(Bot), request.BotId);
        }

        var set = EvalSet.Create(tenantId, request.BotId, request.Name, request.Description);
        db.EvalSets.Add(set);
        await db.SaveChangesAsync(cancellationToken);
        return EvalSets.ToDto(set);
    }

    public async Task<EvalSetDto> Handle(UpdateEvalSetCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireEditorTenantId();
        var set = await db.FindEvalSetAsync(request.Id, cancellationToken);

        set.Rename(request.Name, request.Description);
        await db.SaveChangesAsync(cancellationToken);
        return EvalSets.ToDto(set);
    }

    public async Task Handle(DeleteEvalSetCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireEditorTenantId();
        var set = await db.FindEvalSetAsync(request.Id, cancellationToken);

        // The database removes its questions, runs and results with it (ON DELETE CASCADE).
        db.EvalSets.Remove(set);
        await db.SaveChangesAsync(cancellationToken);
    }
}
