using System.Security.Claims;
using Citra.Api.Auth;
using Citra.Application.Common.Security;
using Citra.Application.Features.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Citra.Api.Hubs;

/// <summary>
/// Live updates for the documents page: when a document's status changes (Extracting → … → Ready or Failed), every
/// signed-in user of its company gets the new state at once, instead of the page polling. Server → browser only:
/// clients call nothing here. Each connection joins its company's group on connect, so nobody ever receives another
/// company's documents; the page picks the ones of the bot it shows (the update carries the bot id).
/// </summary>
[Authorize]
public sealed class DocumentsHub : Hub<IDocumentsClient>
{
    /// <summary>Under <see cref="AuthenticationSetup.HubsPath"/>, where browsers may send their token in the address.</summary>
    public const string Path = AuthenticationSetup.HubsPath + "/documents";

    /// <summary>The group of all connections of one company (tenant).</summary>
    public static string TenantGroup(Guid tenantId) => $"tenant:{tenantId}";

    public override async Task OnConnectedAsync()
    {
        // [Authorize] guarantees a valid token; a token without a company would be a bug: refuse the connection.
        if (!Guid.TryParse(Context.User?.FindFirstValue(CitraClaimTypes.TenantId), out var tenantId) || tenantId == Guid.Empty)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId));
        await base.OnConnectedAsync();
    }
}

/// <summary>What the server can call on a connected documents page.</summary>
public interface IDocumentsClient
{
    /// <summary>A document was added or changed (status, counts, name or error): the page adds or replaces its row.</summary>
    Task DocumentChanged(DocumentDto document);

    /// <summary>A document was deleted (by another user, or with its bot): the page removes its row.</summary>
    Task DocumentRemoved(RemovedDocument document);
}

/// <summary>Which document is gone, and from which bot's list.</summary>
public sealed record RemovedDocument(Guid Id, Guid BotId);
