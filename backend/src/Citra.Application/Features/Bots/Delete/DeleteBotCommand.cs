using MediatR;

namespace Citra.Application.Features.Bots.Delete;

/// <summary>
/// Permanently deletes a bot with everything that belongs to it: documents, chunks, processing jobs,
/// conversations, eval sets and runs, and the uploaded files.
/// </summary>
public sealed record DeleteBotCommand(Guid Id) : IRequest;
