using Citra.Domain.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Citra.Infrastructure.Persistence.Configurations;

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.Property(m => m.Role).IsEnumText();
        builder.Property(m => m.Content).HasMaxLength(Message.ContentMaxLength).IsRequired();
        builder.Property(m => m.CostUsd).IsMoney();
        builder.Property(m => m.Feedback).IsEnumText();

        builder.HasMany(m => m.Citations)
            .WithOne()
            .HasForeignKey(c => c.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(m => m.Citations).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Load a conversation's messages in order (also covers the conversation_id foreign key).
        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });
    }
}
