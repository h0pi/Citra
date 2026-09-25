using Citra.Domain.Bots;
using Citra.Domain.Conversations;
using Citra.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Citra.Infrastructure.Persistence.Configurations;

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.Property(c => c.Channel).IsEnumText();
        builder.Property(c => c.VisitorId).HasMaxLength(Conversation.VisitorIdMaxLength);
        builder.Property(c => c.Title).HasMaxLength(Conversation.TitleMaxLength).IsRequired();

        builder.HasOne<Bot>()
            .WithMany()
            .HasForeignKey(c => c.BotId)
            .OnDelete(DeleteBehavior.Cascade);

        // If a user account is deleted, their chats stay (for analytics) but lose the link.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Messages are reached through the read-only list; EF fills the private _messages field.
        builder.HasMany(c => c.Messages)
            .WithOne()
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Messages).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Chat history list per bot, most recent first (also covers the bot_id foreign key).
        builder.HasIndex(c => new { c.BotId, c.UpdatedAt });

        // Widget: find a returning visitor's conversation.
        builder.HasIndex(c => new { c.BotId, c.VisitorId });

        builder.HasIndex(c => c.TenantId);
    }
}
