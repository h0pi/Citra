using MediatR;

namespace Citra.Application.Features.Documents.AddUrl;

/// <summary>
/// Adds a public web page (e.g. a help-center article) to a bot's knowledge. Only the link is saved here;
/// the Worker downloads and reads the page later.
/// </summary>
public sealed record AddUrlDocumentCommand(Guid BotId, string Url) : IRequest<DocumentDto>;
