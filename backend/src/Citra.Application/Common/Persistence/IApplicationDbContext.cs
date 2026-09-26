using Citra.Domain.Bots;
using Citra.Domain.Conversations;
using Citra.Domain.Documents;
using Citra.Domain.Evaluation;
using Citra.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
