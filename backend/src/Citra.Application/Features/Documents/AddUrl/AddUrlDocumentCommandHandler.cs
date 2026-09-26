using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Application.Common.Web;
using Citra.Domain.Bots;
using Citra.Domain.Documents;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Documents.AddUrl;

internal sealed class AddUrlDocumentCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser) : IRequestHandler<AddUrlDocumentCommand, DocumentDto>
{
    public async Task<DocumentDto> Handle(AddUrlDocumentCommand request, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.RequireEditorTenantId();

        if (!await db.Bots.AnyAsync(b => b.Id == request.BotId, cancellationToken))
        {
            throw new NotFoundException(nameof(Bot), request.BotId);
        }

        // "https://Example.com/help#top" and "https://example.com/help" are the same page.
        var url = UrlSafety.Normalize(new Uri(request.Url.Trim()));
        var urlText = url.ToString();

        if (await IsAlreadyAddedAsync(request.BotId, urlText, cancellationToken))
        {
            throw DuplicateError();
        }

        var document = Document.FromUrl(tenantId, request.BotId, url);
        db.Documents.Add(document);
        db.ProcessingJobs.Add(ProcessingJob.For(document.Id));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Added twice at the same moment: the unique index (bot_id, source_url) let only one in.
            if (await IsAlreadyAddedAsync(request.BotId, urlText, CancellationToken.None))
            {
                throw DuplicateError();
            }

            throw;
        }

        return DocumentDto.From(document);
    }

    private Task<bool> IsAlreadyAddedAsync(Guid botId, string url, CancellationToken cancellationToken) =>
        db.Documents.AnyAsync(d => d.BotId == botId && d.SourceUrl == url, cancellationToken);

    private static ValidationException DuplicateError() =>
        new(nameof(AddUrlDocumentCommand.Url), "This page is already in this bot.");
}
