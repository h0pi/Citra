using Citra.Application.Features.Auth.Demo;
using Citra.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Citra.Worker.Processing;

/// <summary>
/// Cleans up the public demo once a night at <see cref="DemoOptions.ResetHourUtc"/> (UTC), in its own DI scope.
/// Does nothing on servers without a demo. An error never stops the Worker: it is logged and retried the next night.
/// </summary>
public sealed class DemoResetService(
    IServiceScopeFactory scopeFactory,
    IOptions<DemoOptions> options,
    TimeProvider timeProvider,
    ILogger<DemoResetService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.TenantSlug))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = timeProvider.GetUtcNow();
            try
            {
                await Task.Delay(NextRun(now, options.Value.ResetHourUtc) - now, timeProvider, stoppingToken);
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<IDemoReset>().RunAsync(stoppingToken);
                logger.LogInformation(
                    "Demo reset: removed {DemoUsers} demo users and {Conversations} chats", result.DemoUsers, result.Conversations);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The demo reset failed; trying again next night");
            }
        }
    }

    /// <summary>The next moment it is <paramref name="hourUtc"/>:00 UTC, after <paramref name="now"/>.</summary>
    public static DateTimeOffset NextRun(DateTimeOffset now, int hourUtc)
    {
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddHours(hourUtc);
        return today > now ? today : today.AddDays(1);
    }
}
