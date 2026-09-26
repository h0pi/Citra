using Citra.Application.Common.Security;
using Citra.Infrastructure;
using Citra.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace Citra.IntegrationTests.Infrastructure;

/// <summary>
/// Starts one throwaway PostgreSQL + pgvector container for all database tests, applies the real
/// migrations, and wires the real Infrastructure services against it. Tests pick "who is logged in"
/// per scope via <see cref="OpenScope"/>. Requires Docker to be running.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    // Same image as docker-compose.dev.yml, so tests run on exactly the database we develop against.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();

    private ServiceProvider? _services;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = _container.GetConnectionString(),
                ["Anthropic:ChatModel"] = "test-model",
                ["Anthropic:MaxOutputTokens"] = "1024",
                ["Voyage:BaseUrl"] = "https://voyage.test/",
                ["Voyage:EmbeddingModel"] = "test-embedding",
                ["Voyage:EmbeddingDimensions"] = "1024",
                ["Voyage:BatchSize"] = "8",
                ["Storage:LocalRootPath"] = Path.GetTempPath(),
                ["Storage:MaxUploadSizeMb"] = "20",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        // Tests decide who the current user is, per scope.
        services.AddScoped<TestCurrentUser>();
        services.Replace(ServiceDescriptor.Scoped<ICurrentUser>(sp => sp.GetRequiredService<TestCurrentUser>()));

        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await _services.ApplyDatabaseMigrationsAsync();
    }

    /// <summary>A unit of work acting as a user of <paramref name="tenantId"/> (null = nobody logged in).</summary>
    public DbScope OpenScope(Guid? tenantId)
    {
        var scope = _services!.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>().TenantId = tenantId;
        return new DbScope(scope);
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}

/// <summary>A test's database session: dispose it to end the unit of work.</summary>
public sealed class DbScope(AsyncServiceScope scope) : IAsyncDisposable
{
    public CitraDbContext Db { get; } = scope.ServiceProvider.GetRequiredService<CitraDbContext>();

    public IServiceProvider Services => scope.ServiceProvider;

    public ValueTask DisposeAsync() => scope.DisposeAsync();
}

/// <summary>Shares one database container across all test classes in the "Database" collection.</summary>
[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "Database";
}
