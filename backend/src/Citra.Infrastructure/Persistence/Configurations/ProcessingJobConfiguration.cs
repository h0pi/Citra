using Citra.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Citra.Infrastructure.Persistence.Configurations;

internal sealed class ProcessingJobConfiguration : IEntityTypeConfiguration<ProcessingJob>
{
    public void Configure(EntityTypeBuilder<ProcessingJob> builder)
    {
        builder.Property(j => j.Status).IsEnumText();
        builder.Property(j => j.LastError).HasMaxLength(ProcessingJob.LastErrorMaxLength);

        // Every claim counts a new attempt, so the attempt number tells whose job it is. As a concurrency token,
        // a worker's final save (Succeeded/Failed) only goes through while the job is still on ITS attempt:
        // if another worker took the job over meanwhile, the save fails and its whole transaction rolls back.
        builder.Property(j => j.Attempts).IsConcurrencyToken();

        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(j => j.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        // The Worker polls "pending jobs that are due" every few seconds.
        builder.HasIndex(j => new { j.Status, j.NextAttemptAt });
    }
}
