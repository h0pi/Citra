using MediatR;

namespace Citra.Application.Features.Bots.GetById;

/// <summary>One bot with all its settings, for the bot settings page.</summary>
public sealed record GetBotByIdQuery(Guid Id) : IRequest<BotDto>;
