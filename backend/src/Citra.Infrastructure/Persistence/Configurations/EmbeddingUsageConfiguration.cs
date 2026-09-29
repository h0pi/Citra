using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.Domain.Usage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Citra.Infrastructure.Persistence.Configurations;

internal sealed class EmbeddingUsageConfiguration : IEntityTypeConfiguration<EmbeddingUsage>
{
    public void Configure(EntityTypeBuilder<EmbeddingUsage> builder)
    {
        builder.Property(u => u.Purpose).IsEnumText();
        builder.Property(u => u.Model).HasMaxLength(EmbeddingUsage.ModelMaxLength).IsRequired();

        // More decimals than other money columns: one question costs about $0.00000024, which 6 decimals round to 0.
        builder.Property(u => u.CostUsd).HasPrecision(18, 10);

        // Deleting a company removes its usage; deleting a bot or document keeps the record (money was spent).
        builder.HasOne<Tenant>().WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Bot>().WithMany().HasForeignKey(u => u.BotId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Document>().WithMany().HasForeignKey(u => u.DocumentId).OnDelete(DeleteBehavior.SetNull);

        // Dashboard queries: a company's costs over a date range, and per bot over a date range.
        builder.HasIndex(u => new { u.TenantId, u.CreatedAt });
        builder.HasIndex(u => new { u.BotId, u.CreatedAt });
        builder.HasIndex(u => u.DocumentId);
    }
}
