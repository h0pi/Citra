using Citra.Application.Features.Chat.Conversations;
using Citra.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Citra.Worker.Processing;

/// <summary>
/// Deletes chats past <see cref="RetentionOptions.ConversationDays"/> when the Worker starts and then once a day,
/// in its own DI scope. Does nothing when chats are kept forever. An error never stops the Worker: it is logged and
/// tried again the next day.
/// </summary>
public sealed class ConversationCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<RetentionOptions> options,
    TimeProvider timeProvider,
    ILogger<ConversationCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.ConversationDays is null)
        {
            return;
        }

        using var daily = new PeriodicTimer(TimeSpan.FromDays(1), timeProvider);
        try
        {
            do
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var deleted = await scope.ServiceProvider.GetRequiredService<IConversationCleanup>()
                        .DeleteExpiredAsync(stoppingToken);
                    logger.LogInformation("Retention: deleted {Conversations} chats older than {Days} days",
                        deleted, options.Value.ConversationDays);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Deleting old chats failed; trying again tomorrow");
                }
            }
            while (await daily.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The Worker is stopping.
        }
    }
}
