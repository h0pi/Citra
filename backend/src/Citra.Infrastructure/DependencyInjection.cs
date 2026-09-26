using Citra.Application.Common.Persistence;
using Citra.Application.Common.Storage;
using Citra.Infrastructure.HealthChecks;
using Citra.Infrastructure.Identity;
using Citra.Infrastructure.Options;
using Citra.Infrastructure.Persistence;
using Citra.Infrastructure.Persistence.Interceptors;
using Citra.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Pgvector.Npgsql;

namespace Citra.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the Infrastructure layer: database, file storage, AI clients, auth services.
    /// Each area is added here as it gets implemented.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Typed settings, validated at startup (shared by Api and Worker).
        services.AddValidatedOptions<AnthropicOptions>(configuration, AnthropicOptions.SectionName);
        services.AddValidatedOptions<VoyageOptions>(configuration, VoyageOptions.SectionName);
        services.AddValidatedOptions<StorageOptions>(configuration, StorageOptions.SectionName);

        var connectionString = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'Database' is not configured.");
        }

        // One shared, pooled PostgreSQL connection source, used by EF Core and by raw SQL/health checks.
        // pgvector must be enabled on both levels: here (Npgsql type mapping) and in EF Core below.
        services.AddNpgsqlDataSource(connectionString, dataSource => dataSource.UseVector());

        // The app's clock. TryAdd: tests (or a host) can register a fake TimeProvider first.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<AuditTimestampsInterceptor>();

        services.AddDbContext<CitraDbContext>((serviceProvider, options) => options
            .UseNpgsql(
                serviceProvider.GetRequiredService<NpgsqlDataSource>(),
                npgsql => npgsql.UseVector())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(serviceProvider.GetRequiredService<AuditTimestampsInterceptor>()));

        // Handlers depend on the interface; it resolves to the same scoped DbContext instance,
        // so handlers, Identity and other services share one unit of work per request.
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<CitraDbContext>());

        // Accounts & tokens. Registered for every host because the Application layer's handlers
        // (e.g. RegisterCommandHandler) depend on them and .NET validates all registrations at startup.
        // The Worker never uses them, so it needs no JWT settings; the API registers and validates JwtOptions.
        services.AddCitraIdentity();

        // Uploaded files. Stateless, so one instance serves everyone.
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        return services;
    }
}
