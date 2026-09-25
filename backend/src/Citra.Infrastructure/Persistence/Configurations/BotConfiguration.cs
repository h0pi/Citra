using Citra.Domain.Bots;
using Citra.Domain.Common;
using Citra.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Citra.Infrastructure.Persistence.Configurations;

internal sealed class BotConfiguration : IEntityTypeConfiguration<Bot>
{
    private const int ModelMaxLength = 100;
    private const int WidgetColorMaxLength = 20;

    public void Configure(EntityTypeBuilder<Bot> builder)
    {
        builder.Property(b => b.Name).HasMaxLength(Bot.NameMaxLength).IsRequired();
        builder.Property(b => b.Slug).HasMaxLength(Slug.MaxLength).IsRequired();
        builder.Property(b => b.Description).HasMaxLength(Bot.DescriptionMaxLength);
        builder.Property(b => b.SystemPrompt).HasMaxLength(Bot.SystemPromptMaxLength);
        builder.Property(b => b.WelcomeMessage).HasMaxLength(Bot.WelcomeMessageMaxLength).IsRequired();
        builder.Property(b => b.Model).HasMaxLength(ModelMaxLength);
        builder.Property(b => b.WidgetColor).HasMaxLength(WidgetColorMaxLength).IsRequired();

        // List<string> → PostgreSQL text[] (native array column, no join table needed).
        builder.Property(b => b.AllowedDomains).IsRequired();
        builder.Property(b => b.StarterQuestions).IsRequired();

        // Deleting a tenant deletes its bots (and, through them, everything below).
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(b => b.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        // Slug is unique within a tenant (two companies may both have a "support" bot).
        // Also serves as the tenant_id lookup index, since tenant_id is its first column.
        builder.HasIndex(b => new { b.TenantId, b.Slug }).IsUnique();
    }
}
