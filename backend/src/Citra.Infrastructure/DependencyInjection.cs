using Citra.Application.Common.Chunking;
using Citra.Application.Common.Embeddings;
using Citra.Application.Common.Extraction;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Storage;
using Citra.Application.Features.Documents.Processing;
using Citra.Infrastructure.Chunking;
using Citra.Infrastructure.Embeddings;
using Citra.Infrastructure.Extraction;
using Citra.Infrastructure.HealthChecks;
using Citra.Infrastructure.Identity;
using Citra.Infrastructure.Options;
using Citra.Infrastructure.Persistence;
using Citra.Infrastructure.Persistence.Interceptors;
using Citra.Infrastructure.Processing;
using Citra.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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
        services.AddValidatedOptions<ProcessingOptions>(configuration, ProcessingOptions.SectionName);

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
        services.AddSingleton<IUploadLimits, ConfiguredUploadLimits>();

        // Text extraction (used by the Worker). Stateless; the fetcher owns one pooled HttpClient.
        services.AddSingleton<WebPageFetcher>();
        services.AddSingleton<ITextExtractor, PdfTextExtractor>();
        services.AddSingleton<ITextExtractor, DocxTextExtractor>();
        services.AddSingleton<ITextExtractor, PlainTextExtractor>();
        services.AddSingleton<ITextExtractor, UrlTextExtractor>();
        services.AddSingleton<ITextExtractorFactory, TextExtractorFactory>();

        // Chunk sizing. One shared tokenizer: loading its vocabulary is slow, using it is thread-safe.
        services.AddSingleton<ITokenCounter, TiktokenTokenCounter>();

        // Embeddings (Voyage AI) through a typed HttpClient: pooled connections, base address from settings,
        // and automatic retries (see VoyageResilience). Timeouts are set there, per attempt and in total.
        services.AddHttpClient<IEmbeddingService, VoyageEmbeddingService>((provider, client) =>
            {
                var voyage = provider.GetRequiredService<IOptions<VoyageOptions>>().Value;
                client.BaseAddress = new Uri(voyage.BaseUrl.EndsWith('/') ? voyage.BaseUrl : voyage.BaseUrl + "/");
                client.Timeout = Timeout.InfiniteTimeSpan; // the resilience pipeline owns timeouts (retries need time)
            })
            .AddVoyageResilience();

        // Background processing: jobs are claimed with row locks, one unit of work per job.
        services.AddScoped<IProcessingJobQueue, ProcessingJobQueue>();

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        return services;
    }
}
