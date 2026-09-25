using Citra.Domain.Conversations;
using Citra.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Citra.Infrastructure.Persistence.Configurations;

internal sealed class MessageCitationConfiguration : IEntityTypeConfiguration<MessageCitation>
{
    public void Configure(EntityTypeBuilder<MessageCitation> builder)
    {
        builder.Property(c => c.DocumentName).HasMaxLength(MessageCitation.DocumentNameMaxLength).IsRequired();
        builder.Property(c => c.Snippet).HasMaxLength(MessageCitation.SnippetMaxLength).IsRequired();

        // Citations keep their own copy of name/page/snippet, so when a document is re-processed
        // (old chunks deleted) or deleted, the old answer still shows its sources — only the links go null.
        builder.HasOne<Chunk>()
            .WithMany()
            .HasForeignKey(c => c.ChunkId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.SetNull);

        // One [n] marker per number in an answer (also covers the message_id foreign key).
        builder.HasIndex(c => new { c.MessageId, c.Number }).IsUnique();
    }
}
