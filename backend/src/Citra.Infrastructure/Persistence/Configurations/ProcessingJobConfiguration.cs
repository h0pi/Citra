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

        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(j => j.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        // The Worker polls "pending jobs that are due" every few seconds.
        builder.HasIndex(j => new { j.Status, j.NextAttemptAt });
    }
}
