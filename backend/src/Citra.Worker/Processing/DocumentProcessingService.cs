using Citra.Application.Features.Documents.Processing;
using Citra.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Citra.Worker.Processing;

/// <summary>
/// The Worker's main loop: checks the processing_jobs table every <see cref="ProcessingOptions.PollInterval"/>
/// and runs every due job, one after another, each in its own DI scope (fresh DbContext per job).
/// An error never stops the loop: it is logged and the next poll tries again. Stops cleanly on shutdown.
/// </summary>
public sealed class DocumentProcessingService(
    IServiceScopeFactory scopeFactory,
    IOptions<ProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<DocumentProcessingService> logger) : BackgroundService
{
    private readonly TimeSpan _pollInterval = options.Value.PollInterval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Document processing started, checking for jobs every {PollInterval}", _pollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDueJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // E.g. the database is down: keep the Worker alive and try again on the next poll.
                logger.LogError(ex, "Checking for processing jobs failed; trying again in {PollInterval}", _pollInterval);
            }

            try
            {
                await Task.Delay(_pollInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("Document processing stopped");
    }

    /// <summary>Runs jobs until none is due, so a batch of uploads does not wait one poll interval per document.</summary>
    private async Task RunDueJobsAsync(CancellationToken stoppingToken)
    {
        bool ranJob;
        do
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var runner = scope.ServiceProvider.GetRequiredService<IProcessingJobRunner>();
            ranJob = await runner.RunNextAsync(stoppingToken);
        }
        while (ranJob && !stoppingToken.IsCancellationRequested);
    }
}
