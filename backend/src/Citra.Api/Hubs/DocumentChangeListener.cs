using Citra.Application.Common.Persistence;
using Citra.Application.Features.Documents;
using Citra.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Citra.Api.Hubs;

/// <summary>
/// Brings the Worker's document changes to the browsers: listens on PostgreSQL's "document_changes" channel (fed by
/// a trigger on the documents table) and sends each changed document's current state to its company's group on
/// <see cref="DocumentsHub"/>. One dedicated connection; if it breaks (database restart, network), it reconnects.
/// Every API instance listens, and each informs its own connected browsers.
/// </summary>
internal sealed class DocumentChangeListener(
    NpgsqlDataSource dataSource,
    IServiceScopeFactory scopeFactory,
    IHubContext<DocumentsHub, IDocumentsClient> hub,
    TimeProvider timeProvider,
    ILogger<DocumentChangeListener> logger) : BackgroundService
{
    /// <summary>Wait before listening again after the connection was lost.</summary>
    internal static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    /// <summary>After this long without notifications the connection is checked (a silently dead one never wakes up).</summary>
    internal static readonly TimeSpan IdleCheck = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ListenAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Changes made while not listening are not sent; the page shows them when it next loads the list.
                logger.LogWarning(ex, "Lost the live document updates connection; listening again in {ReconnectDelay}", ReconnectDelay);
            }

            try
            {
                await Task.Delay(ReconnectDelay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ListenAsync(CancellationToken stoppingToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(stoppingToken);

        // Raised inside WaitAsync; handled right after it returns, one notification after another.
        var pending = new Queue<string>();
        connection.Notification += (_, notification) => pending.Enqueue(notification.Payload);

        await using (var listen = new NpgsqlCommand($"LISTEN {DocumentChangeNotification.Channel}", connection))
        {
            await listen.ExecuteNonQueryAsync(stoppingToken);
        }

        logger.LogInformation("Listening for document changes to send to the documents page");

        while (true)
        {
            if (!await connection.WaitAsync(IdleCheck, stoppingToken))
            {
                // Nothing for a while: make sure the connection still works (throws if it does not → reconnect).
                await using var check = new NpgsqlCommand("SELECT 1", connection);
                await check.ExecuteScalarAsync(stoppingToken);
            }

            while (pending.TryDequeue(out var payload))
            {
                await ForwardAsync(payload, stoppingToken);
            }
        }
    }

    private async Task ForwardAsync(string payload, CancellationToken stoppingToken)
    {
        var change = DocumentChangeNotification.Parse(payload);
        if (change is null)
        {
            logger.LogWarning("Ignored a document change notification that could not be read: {Payload}", payload);
            return;
        }

        try
        {
            var group = hub.Clients.Group(DocumentsHub.TenantGroup(change.TenantId));
            if (change.Deleted)
            {
                await group.DocumentRemoved(new RemovedDocument(change.DocumentId, change.BotId));
                return;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var document = await db.Documents
                .IgnoreQueryFilters([QueryFilters.Tenant]) // no user here: the notification names the company
                .AsNoTracking()
                .SingleOrDefaultAsync(d => d.Id == change.DocumentId, stoppingToken);

            // Deleted meanwhile: its own "deleted" notification follows.
            if (document is not null)
            {
                await group.DocumentChanged(DocumentDto.From(document));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One document's update failing must not stop the updates of all the others.
            logger.LogWarning(ex, "Could not send the change of document {DocumentId} to the documents page", change.DocumentId);
        }
    }
}
