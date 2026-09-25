using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Citra.Infrastructure.Persistence.Configurations;

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.Property(d => d.FileName).HasMaxLength(Document.FileNameMaxLength).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(Document.ContentTypeMaxLength).IsRequired();
        builder.Property(d => d.SourceType).IsEnumText();
        builder.Property(d => d.SourceUrl).HasMaxLength(Document.SourceUrlMaxLength);
        builder.Property(d => d.StoragePath).HasMaxLength(Document.StoragePathMaxLength);
        builder.Property(d => d.ContentHash).HasMaxLength(Document.ContentHashLength).IsFixedLength();
        builder.Property(d => d.Status).IsEnumText();
        builder.Property(d => d.ErrorMessage).HasMaxLength(Document.ErrorMessageMaxLength);

        builder.HasOne<Bot>()
            .WithMany()
            .HasForeignKey(d => d.BotId)
            .OnDelete(DeleteBehavior.Cascade);

        // Document list per bot, newest first (also covers the bot_id foreign key).
        builder.HasIndex(d => new { d.BotId, d.CreatedAt });

        // The same file can't be uploaded twice to one bot. URL documents have no hash (NULLs never collide).
        builder.HasIndex(d => new { d.BotId, d.ContentHash }).IsUnique();

        builder.HasIndex(d => d.TenantId);
    }
}
