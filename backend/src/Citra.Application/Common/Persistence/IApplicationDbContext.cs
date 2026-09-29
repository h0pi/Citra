using Citra.Domain.Bots;
using Citra.Domain.Conversations;
using Citra.Domain.Documents;
using Citra.Domain.Evaluation;
using Citra.Domain.Tenants;
using Citra.Domain.Usage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Citra.Application.Common.Persistence;

/// <summary>
/// The database as seen by use-case handlers: domain tables plus SaveChanges.
/// Implemented by Infrastructure's CitraDbContext; handlers never see Npgsql, pgvector or Identity details.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Tenant> Tenants { get; }

    DbSet<Bot> Bots { get; }

    DbSet<Document> Documents { get; }

    DbSet<Chunk> Chunks { get; }

    DbSet<ProcessingJob> ProcessingJobs { get; }

    DbSet<Conversation> Conversations { get; }

    DbSet<Message> Messages { get; }

    DbSet<MessageCitation> MessageCitations { get; }

    DbSet<EvalSet> EvalSets { get; }

    DbSet<EvalQuestion> EvalQuestions { get; }

    DbSet<EvalRun> EvalRuns { get; }

    DbSet<EvalResult> EvalResults { get; }

    DbSet<EmbeddingUsage> EmbeddingUsages { get; }

    /// <summary>For the rare handler that must combine a bulk ExecuteDelete/ExecuteUpdate with SaveChanges in one transaction.</summary>
    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
