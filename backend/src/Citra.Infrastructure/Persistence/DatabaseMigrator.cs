using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Citra.Infrastructure.Persistence;

public static class DatabaseMigrator
{
    /// <summary>
    /// Applies any pending EF Core migrations. Meant for Development only: in production, schema
    /// changes are a deliberate deployment step, not something every app instance does on startup.
    /// If the model has changes that were never turned into a migration, EF Core refuses to run —
    /// a useful reminder to run `dotnet ef migrations add`.
    /// </summary>
    public static async Task ApplyDatabaseMigrationsAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CitraDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DatabaseMigrator));

        // On a brand-new database the migrations history table doesn't exist yet, and EF Core's own
        // existence check probes it with a query that fails — logged as a (harmless but misleading) ERROR.
        // Creating the table first with EF's own "create if not exists" script avoids that noise.
        var historyRepository = db.GetService<IHistoryRepository>();
        await db.Database.ExecuteSqlRawAsync(historyRepository.GetCreateIfNotExistsScript(), cancellationToken);

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("Database schema is up to date");
            return;
        }

        logger.LogInformation(
            "Applying {Count} pending migration(s): {Migrations}",
            pending.Count, string.Join(", ", pending));

        await db.Database.MigrateAsync(cancellationToken);

        logger.LogInformation("Database migrations applied");
    }
}
