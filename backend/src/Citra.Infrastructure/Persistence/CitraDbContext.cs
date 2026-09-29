using System.Reflection;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Bots;
using Citra.Domain.Common;
using Citra.Domain.Conversations;
using Citra.Domain.Documents;
using Citra.Domain.Evaluation;
using Citra.Domain.Tenants;
using Citra.Domain.Usage;
using Citra.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Citra.Infrastructure.Persistence;

/// <summary>
/// The EF Core database context: Citra's tables plus the ASP.NET Identity tables (users, roles, logins).
/// Table/column names are snake_case (see DependencyInjection), per-entity mapping lives in Configurations/.
/// Tenant isolation: every query on tenant-owned data is filtered to the current user's tenant,
/// and saving another tenant's data is blocked.
/// </summary>
public sealed class CitraDbContext(DbContextOptions<CitraDbContext> options, ICurrentUser currentUser)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IApplicationDbContext
{
    private static readonly MethodInfo ApplyTenantFilterMethod = typeof(CitraDbContext)
        .GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Bot> Bots => Set<Bot>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<Chunk> Chunks => Set<Chunk>();

    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<MessageCitation> MessageCitations => Set<MessageCitation>();

    public DbSet<EvalSet> EvalSets => Set<EvalSet>();

    public DbSet<EvalQuestion> EvalQuestions => Set<EvalQuestion>();

    public DbSet<EvalRun> EvalRuns => Set<EvalRun>();

    public DbSet<EvalResult> EvalResults => Set<EvalResult>();

    public DbSet<EmbeddingUsage> EmbeddingUsages => Set<EmbeddingUsage>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>
    /// Read by the tenant query filter at query time (EF Core re-evaluates context members per query),
    /// so one cached model serves every tenant. Null when nobody is logged in → filtered queries return nothing.
    /// </summary>
    private Guid? CurrentTenantId => currentUser.TenantId;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureNoCrossTenantWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureNoCrossTenantWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity tables first, so our configurations can adjust them afterwards.
        base.OnModelCreating(builder);

        builder.HasPostgresExtension("vector");

        builder.ApplyConfigurationsFromAssembly(typeof(CitraDbContext).Assembly);

        foreach (var entityType in builder.Model.GetEntityTypes().ToList())
        {
            // Ids are generated in code (UUID v7), never by the database. Telling EF this matters:
            // otherwise a new child added through a navigation (e.g. conversation.AddUserMessage)
            // has a non-empty Id, EF assumes it already exists, and SaveChanges runs an UPDATE instead of an INSERT.
            if (typeof(Entity).IsAssignableFrom(entityType.ClrType))
            {
                builder.Entity(entityType.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
            }

            if (typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            {
                ApplyTenantFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [builder]);
            }
        }
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder builder)
        where TEntity : class, ITenantOwned =>
        builder.Entity<TEntity>().HasQueryFilter(QueryFilters.Tenant, e => e.TenantId == CurrentTenantId);

    /// <summary>
    /// Second line of defence (reads are already filtered): a logged-in user can never insert or change
    /// data belonging to another tenant, even if a bug put the wrong TenantId on an entity.
    /// Code without a user (background worker, sign-up) is not restricted here.
    /// </summary>
    private void EnsureNoCrossTenantWrites()
    {
        if (CurrentTenantId is not { } tenantId)
        {
            return;
        }

        var violation = ChangeTracker.Entries<ITenantOwned>()
            .FirstOrDefault(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                                 && e.Entity.TenantId != tenantId);

        if (violation is not null)
        {
            throw new InvalidOperationException(
                $"Blocked a cross-tenant write: {violation.Entity.GetType().Name} belongs to tenant " +
                $"{violation.Entity.TenantId}, but the current user's tenant is {tenantId}.");
        }
    }
}
