using Citra.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Citra.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.TokenHash).HasMaxLength(RefreshToken.TokenHashLength).IsFixedLength().IsRequired();

        // Lookup by hash on every refresh; unique so a hash can never map to two rows.
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // Revoking a whole login session (logout / theft detection).
        builder.HasIndex(t => t.FamilyId);

        // Deleting a user deletes their tokens (also covers the user_id foreign key).
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
