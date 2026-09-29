using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Citra.Application.Features.Documents.Processing;
using Citra.Infrastructure.Options;
using Citra.Worker.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Citra.IntegrationTests.Processing;

/// <summary>
/// C-140: the Worker's polling loop, driven with a fake job runner (no database): waits the poll interval when
/// idle, runs due jobs back to back, gives every job its own scope, survives errors and stops cleanly.
/// </summary>
public sealed class DocumentProcessingServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task When_nothing_is_due_it_checks_again_after_the_poll_interval()
    {
        var runs = new RunLog(_ => false);
        await using var worker = await StartAsync(runs, pollMilliseconds: 150);

        await runs.WaitForAsync(count: 4);

        // 3 pauses of 150 ms between 4 checks: it waits, it does not spin.
        var times = runs.Times;
        (times[3] - times[0]).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(400));
        for (var i = 1; i < 4; i++)
        {
            (times[i] - times[i - 1]).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(130), $"pause {i}");
        }
    }

    [Fact]
    public async Task Due_jobs_run_back_to_back_then_it_waits()
    {
        // Three jobs due, then nothing: all three run at once, the 4th check finds nothing, and the next
        // check only comes after the (here: one minute) poll interval.
        var runs = new RunLog(call => call <= 3);
        await using var worker = await StartAsync(runs, pollMilliseconds: 60_000);

        await runs.WaitForAsync(count: 4);
        await Task.Delay(500, Ct);

        runs.Count.ShouldBe(4);
        (runs.Times[3] - runs.Times[0]).ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Every_job_runs_in_its_own_scope()
    {
        var runs = new RunLog(call => call <= 3);
        await using var worker = await StartAsync(runs, pollMilliseconds: 60_000);

        await runs.WaitForAsync(count: 4);

        runs.RunnerIds.Distinct().Count().ShouldBe(4, "a new runner (and so a new DbContext) per job");
        runs.DisposedRunners.ShouldBe(4, "each scope is disposed after its job");
    }

    [Fact]
    public async Task An_error_is_logged_and_polling_goes_on()
    {
        var runs = new RunLog(call => call == 1 ? throw new InvalidOperationException("database is down") : false);
        await using var worker = await StartAsync(runs, pollMilliseconds: 100);

        await runs.WaitForAsync(count: 3);

        var error = worker.Logs.Where(l => l.Level == LogLevel.Error).ShouldHaveSingleItem();
        error.Exception.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe("database is down");
        error.Message.ShouldContain("trying again");
        worker.Service.ExecuteTask!.IsCompleted.ShouldBeFalse("the loop is still running");
    }

    [Fact]
    public async Task It_stops_promptly_while_waiting_for_the_next_poll()
    {
        var runs = new RunLog(_ => false);
        var worker = await StartAsync(runs, pollMilliseconds: 60_000);
        await runs.WaitForAsync(count: 1);

        var stopwatch = Stopwatch.StartNew();
        await worker.DisposeAsync();

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2), "it does not sit out the one-minute pause");
        worker.Service.ExecuteTask!.Status.ShouldBe(TaskStatus.RanToCompletion);
        worker.Logs.ShouldNotContain(l => l.Level >= LogLevel.Warning);
        worker.Logs.ShouldContain(l => l.Message == "Document processing stopped");
    }

    [Fact]
    public async Task Stopping_cancels_the_running_job_without_logging_an_error()
    {
        var jobStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runs = new RunLog(_ => false, async ct =>
        {
            jobStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct); // a long job; only shutdown ends it
            return true;
        });
        var worker = await StartAsync(runs, pollMilliseconds: 60_000);
        await jobStarted.Task.WaitAsync(WaitLimit, Ct);

        var stopwatch = Stopwatch.StartNew();
        await worker.DisposeAsync();

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        worker.Service.ExecuteTask!.Status.ShouldBe(TaskStatus.RanToCompletion);
        worker.Logs.ShouldNotContain(l => l.Level >= LogLevel.Warning);
        runs.DisposedRunners.ShouldBe(1, "the cancelled job's scope is still disposed");
    }

    [Fact]
    public void Defaults_poll_every_two_seconds_and_lock_jobs_for_fifteen_minutes()
    {
        var options = new ProcessingOptions();

        options.PollInterval.ShouldBe(TimeSpan.FromSeconds(2));
        options.LockDuration.ShouldBe(TimeSpan.FromMinutes(15));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1440, true)]
    [InlineData(1441, false)]
    public void The_lock_duration_setting_is_validated(int minutes, bool valid)
    {
        var options = new ProcessingOptions { LockDurationMinutes = minutes };

        Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true)
            .ShouldBe(valid);
    }

    [Theory]
    [InlineData(99, false)]
    [InlineData(100, true)]
    [InlineData(2000, true)]
    [InlineData(60_000, true)]
    [InlineData(60_001, false)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void The_poll_interval_setting_is_validated(int milliseconds, bool valid)
    {
        var options = new ProcessingOptions { PollIntervalMilliseconds = milliseconds };

        Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true)
            .ShouldBe(valid);
    }

    // ---------- helpers ----------

    private static async Task<RunningWorker> StartAsync(RunLog runs, int pollMilliseconds)
    {
        var logs = new ConcurrentQueue<LogEntry>();
        var services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(new ListLoggerProvider(logs)).SetMinimumLevel(LogLevel.Debug))
            .AddSingleton(runs)
            .AddScoped<IProcessingJobRunner, FakeRunner>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var service = new DocumentProcessingService(
            services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new ProcessingOptions { PollIntervalMilliseconds = pollMilliseconds }),
            TimeProvider.System,
            services.GetRequiredService<ILogger<DocumentProcessingService>>());

        await service.StartAsync(Ct);
        return new RunningWorker(service, services, logs);
    }

    private sealed class RunningWorker(DocumentProcessingService service, ServiceProvider services, ConcurrentQueue<LogEntry> logs)
        : IAsyncDisposable
    {
        public DocumentProcessingService Service => service;

        public IReadOnlyList<LogEntry> Logs => [.. logs];

        public async ValueTask DisposeAsync()
        {
            await service.StopAsync(CancellationToken.None);
            service.Dispose();
            await services.DisposeAsync();
        }
    }

    /// <summary>What the fake runner does on the n-th call (1-based), and a record of every call.</summary>
    private sealed class RunLog(Func<int, bool> result, Func<CancellationToken, Task<bool>>? run = null)
    {
        private readonly Lock _gate = new();
        private readonly List<TimeSpan> _times = [];
        private readonly List<Guid> _runnerIds = [];
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private int _disposed;

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _times.Count;
                }
            }
        }

        public IReadOnlyList<TimeSpan> Times
        {
            get
            {
                lock (_gate)
                {
                    return [.. _times];
                }
            }
        }

        public IReadOnlyList<Guid> RunnerIds
        {
            get
            {
                lock (_gate)
                {
                    return [.. _runnerIds];
                }
            }
        }

        public int DisposedRunners => Volatile.Read(ref _disposed);

        public Task<bool> RunAsync(Guid runnerId, CancellationToken cancellationToken)
        {
            int call;
            lock (_gate)
            {
                _times.Add(_clock.Elapsed);
                _runnerIds.Add(runnerId);
                call = _times.Count;
            }

            return run is not null ? run(cancellationToken) : Task.FromResult(result(call));
        }

        public void RunnerDisposed() => Interlocked.Increment(ref _disposed);

        public async Task WaitForAsync(int count)
        {
            var deadline = Stopwatch.StartNew();
            while (Count < count)
            {
                deadline.Elapsed.ShouldBeLessThan(WaitLimit, $"waited for {count} runs, saw {Count}");
                await Task.Delay(10, Ct);
            }
        }
    }

    private sealed class FakeRunner(RunLog log) : IProcessingJobRunner, IDisposable
    {
        private readonly Guid _id = Guid.NewGuid();

        public Task<bool> RunNextAsync(CancellationToken cancellationToken) => log.RunAsync(_id, cancellationToken);

        public void Dispose() => log.RunnerDisposed();
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class ListLoggerProvider(ConcurrentQueue<LogEntry> logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ListLogger(logs);

        public void Dispose()
        {
        }
    }

    private sealed class ListLogger(ConcurrentQueue<LogEntry> logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            logs.Enqueue(new LogEntry(logLevel, formatter(state, exception), exception));
    }
}
