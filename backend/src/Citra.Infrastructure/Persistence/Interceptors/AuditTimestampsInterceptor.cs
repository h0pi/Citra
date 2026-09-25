using Citra.Domain.Common;
using Citra.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Citra.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Sets CreatedAt / UpdatedAt automatically on every save, so application code never has to.
/// CreatedAt is written once and then protected: later saves can't change it, even by accident.
/// Time comes from <see cref="TimeProvider"/>, so tests can use a fake clock.
/// </summary>
internal sealed class AuditTimestampsInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyTimestamps(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyTimestamps(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyTimestamps(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();

        foreach (var entry in context.ChangeTracker.Entries<Entity>())
        {
            Stamp(entry, now, e => e.CreatedAt, e => e.UpdatedAt);
        }

        // Identity users aren't domain entities but carry the same kind of timestamp.
        foreach (var entry in context.ChangeTracker.Entries<ApplicationUser>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(u => u.CreatedAt).IsModified = false;
            }
        }
    }

    private static void Stamp<T>(
        EntityEntry<T> entry,
        DateTimeOffset now,
        System.Linq.Expressions.Expression<Func<T, DateTimeOffset>> createdAt,
        System.Linq.Expressions.Expression<Func<T, DateTimeOffset>> updatedAt)
        where T : class
    {
        switch (entry.State)
        {
            case EntityState.Added:
                entry.Property(createdAt).CurrentValue = now;
                entry.Property(updatedAt).CurrentValue = now;
                break;

            case EntityState.Modified:
                entry.Property(updatedAt).CurrentValue = now;
                // Keep the original creation time, whatever the code set.
                entry.Property(createdAt).CurrentValue = entry.Property(createdAt).OriginalValue;
                entry.Property(createdAt).IsModified = false;
                break;
        }
    }
}
