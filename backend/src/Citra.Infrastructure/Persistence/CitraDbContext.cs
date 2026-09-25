using Citra.Domain.Bots;
using Citra.Domain.Common;
using Citra.Domain.Conversations;
using Citra.Domain.Documents;
using Citra.Domain.Evaluation;
using Citra.Domain.Tenants;
using Citra.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Citra.Infrastructure.Persistence;

/// <summary>
/// The EF Core database context: Citra's tables plus the ASP.NET Identity tables (users, roles, logins).
/// Table/column names are snake_case (see DependencyInjection), per-entity mapping lives in Configurations/.
/// </summary>
public sealed class CitraDbContext(DbContextOptions<CitraDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity tables first, so our configurations can adjust them afterwards.
        base.OnModelCreating(builder);

        builder.HasPostgresExtension("vector");

        builder.ApplyConfigurationsFromAssembly(typeof(CitraDbContext).Assembly);

        // Ids are generated in code (UUID v7), never by the database. Telling EF this matters:
        // otherwise a new child added through a navigation (e.g. conversation.AddUserMessage)
        // has a non-empty Id, EF assumes it already exists, and SaveChanges runs an UPDATE instead of an INSERT.
        foreach (var entityType in builder.Model.GetEntityTypes()
                     .Where(e => typeof(Entity).IsAssignableFrom(e.ClrType)))
        {
            builder.Entity(entityType.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
        }
    }
}
