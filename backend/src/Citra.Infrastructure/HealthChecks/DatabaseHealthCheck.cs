using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Citra.Infrastructure.HealthChecks;

/// <summary>
/// Checks that PostgreSQL is reachable and the pgvector extension is installed.
/// </summary>
internal sealed class DatabaseHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand(
                "SELECT extversion FROM pg_extension WHERE extname = 'vector'");

            var pgvectorVersion = await command.ExecuteScalarAsync(cancellationToken);

            return pgvectorVersion is null
                ? HealthCheckResult.Degraded("Database reachable, but the pgvector extension is not installed.")
                : HealthCheckResult.Healthy($"PostgreSQL reachable, pgvector {pgvectorVersion}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database is unreachable.", ex);
        }
    }
}
