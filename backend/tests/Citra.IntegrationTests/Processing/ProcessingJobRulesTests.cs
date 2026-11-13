using Citra.Domain.Documents;

namespace Citra.IntegrationTests.Processing;

/// <summary>The job's own retry rules — growing waits, 3 attempts, and giving up at once.</summary>
public sealed class ProcessingJobRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Lock = TimeSpan.FromMinutes(15);

    [Fact]
    public void Failed_attempts_wait_30_seconds_then_2_minutes_then_give_up()
    {
        var job = ProcessingJob.For(Guid.NewGuid());

        job.Start(Now, Lock);
        job.Fail("first", Now).ShouldBeFalse();
        job.Status.ShouldBe(ProcessingJobStatus.Pending);
        job.NextAttemptAt.ShouldBe(Now.AddSeconds(30));
        job.LockedUntil.ShouldBeNull();

        job.Start(Now.AddSeconds(30), Lock);
        job.Fail("second", Now.AddSeconds(30)).ShouldBeFalse();
        job.NextAttemptAt.ShouldBe(Now.AddSeconds(30).AddMinutes(2));

        job.Start(Now.AddMinutes(3), Lock);
        job.Fail("third", Now.AddMinutes(3)).ShouldBeTrue();
        job.Status.ShouldBe(ProcessingJobStatus.Failed);
        job.CompletedAt.ShouldBe(Now.AddMinutes(3));
        job.LastError.ShouldBe("third");
        job.Attempts.ShouldBe(ProcessingJob.MaxAttempts);
        job.CanStart(Now.AddYears(1)).ShouldBeFalse();
    }

    [Fact]
    public void Giving_up_ends_the_job_at_once_even_with_attempts_left()
    {
        var job = ProcessingJob.For(Guid.NewGuid());
        job.Start(Now, Lock);

        job.GiveUp("scanned PDF", Now.AddSeconds(5));

        job.Status.ShouldBe(ProcessingJobStatus.Failed);
        job.Attempts.ShouldBe(1);
        job.CompletedAt.ShouldBe(Now.AddSeconds(5));
        job.LockedUntil.ShouldBeNull();
        job.LastError.ShouldBe("scanned PDF");
        job.CanStart(Now.AddYears(1)).ShouldBeFalse();
    }

    [Fact]
    public void Only_a_running_job_can_fail_or_give_up()
    {
        var job = ProcessingJob.For(Guid.NewGuid());

        Should.Throw<InvalidOperationException>(() => job.GiveUp("x", Now));
        Should.Throw<InvalidOperationException>(() => job.Fail("x", Now));
        job.Status.ShouldBe(ProcessingJobStatus.Pending);
        job.LastError.ShouldBeNull();
    }

    [Fact]
    public void A_very_long_error_is_cut_to_fit_its_column()
    {
        var job = ProcessingJob.For(Guid.NewGuid());
        job.Start(Now, Lock);

        job.GiveUp(new string('x', ProcessingJob.LastErrorMaxLength + 500), Now);

        job.LastError!.Length.ShouldBe(ProcessingJob.LastErrorMaxLength);
    }
}
