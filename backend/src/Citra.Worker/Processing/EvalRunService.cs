using Citra.Application.Features.Evaluation.Runs;
using Citra.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Citra.Worker.Processing;

/// <summary>
/// Runs pending eval runs in the background, checking every <see cref="ProcessingOptions.PollInterval"/> like the
/// document processing loop. Each run gets its own DI scope; an error never stops the loop.
/// </summary>
public sealed class EvalRunService(
    IServiceScopeFactory scopeFactory,
    IOptions<ProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<EvalRunService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = options.Value.PollInterval;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IEvalRunner>().FailInterruptedAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            // Not fatal: new runs still work; the old ones stay "Running" until the next start.
            logger.LogError(ex, "Could not end eval runs interrupted by a restart");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                bool ran;
                do
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    ran = await scope.ServiceProvider.GetRequiredService<IEvalRunner>().RunNextAsync(stoppingToken);
                }
                while (ran && !stoppingToken.IsCancellationRequested);

                await Task.Delay(pollInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // E.g. the database is down: keep the Worker alive and try again on the next poll.
                logger.LogError(ex, "Checking for eval runs failed; trying again in {PollInterval}", pollInterval);
                await Task.Delay(pollInterval, timeProvider, stoppingToken);
            }
        }
    }
}
