using Citra.Application;
using Citra.Application.Common.Chat;
using Citra.Application.Common.Embeddings;
using Citra.Application.Common.Security;
using Citra.Domain.Users;
using Citra.Infrastructure;
using Citra.Infrastructure.Options;
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

    /// <summary>A private folder for uploaded files, deleted when the tests finish.</summary>
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), $"citra-tests-{Guid.NewGuid():N}");

    /// <summary>The test database, e.g. for hosting the real API against it (see <see cref="ApiFactory"/>).</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>The embedding service every scope gets (see <see cref="FakeEmbeddingService"/>).</summary>
    public FakeEmbeddingService Embeddings { get; } = new();

    /// <summary>The chat model every scope gets (see <see cref="FakeChatModel"/>).</summary>
    public FakeChatModel Chat { get; } = new();

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
                ["Storage:LocalRootPath"] = StorageRoot,
                ["Storage:MaxUploadSizeMb"] = "20",
                // Fake vectors are unrelated to each other (similarity about 0): keep every match unless a test
                // sets its own minimum (see RelevanceThresholdTests).
                ["Retrieval:MinSimilarity"] = "-1",
                ["Retrieval:MinKeywordSimilarity"] = "-1",
                // The model name FakeChatModel's scripted answers report.
                ["Chat:Prices:claude-test:InputPerMillionUsd"] = "2",
                ["Chat:Prices:claude-test:OutputPerMillionUsd"] = "10",
                // Commands that log someone in (register, demo login) issue tokens.
                ["Jwt:Issuer"] = "citra-tests",
                ["Jwt:Audience"] = "citra-tests",
                ["Jwt:SigningKey"] = "test-signing-key-only-for-integration-tests-0123456789",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName)); // the API registers these

        // Tests decide who the current user is, per scope.
        services.AddScoped<TestCurrentUser>();
        services.Replace(ServiceDescriptor.Scoped<ICurrentUser>(sp => sp.GetRequiredService<TestCurrentUser>()));

        // Never Voyage: no network, no cost. (The Voyage client itself is tested with a scripted HTTP handler.)
        services.Replace(ServiceDescriptor.Singleton<IEmbeddingService>(Embeddings));
        services.Replace(ServiceDescriptor.Transient<IChatModel>(_ => Chat)); // never Claude: no key, no cost

        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await _services.ApplyDatabaseMigrationsAsync();
    }

    /// <summary>
    /// A unit of work acting as a user of <paramref name="tenantId"/> with <paramref name="role"/>
    /// (tenantId null = nobody logged in).
    /// </summary>
    public DbScope OpenScope(Guid? tenantId, UserRole role = UserRole.Admin)
    {
        var scope = _services!.CreateAsyncScope();
        var user = scope.ServiceProvider.GetRequiredService<TestCurrentUser>();
        user.TenantId = tenantId;
        user.Role = tenantId is null ? null : role;
        return new DbScope(scope);
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        await _container.DisposeAsync();

        if (Directory.Exists(StorageRoot))
        {
            Directory.Delete(StorageRoot, recursive: true);
        }
    }
}

/// <summary>A test's database session: dispose it to end the unit of work.</summary>
public sealed class DbScope(AsyncServiceScope scope) : IAsyncDisposable
{
    public CitraDbContext Db { get; } = scope.ServiceProvider.GetRequiredService<CitraDbContext>();

    public IServiceProvider Services => scope.ServiceProvider;

    /// <summary>Runs a command or query through MediatR, with the same validation and logging as the API.</summary>
    public Task<TResponse> Send<TResponse>(MediatR.IRequest<TResponse> request) =>
        scope.ServiceProvider.GetRequiredService<MediatR.ISender>().Send(request, TestContext.Current.CancellationToken);

    /// <summary>Runs a streaming request (e.g. a chat answer) through MediatR and collects everything it streams.</summary>
    public async Task<List<TResponse>> Stream<TResponse>(MediatR.IStreamRequest<TResponse> request)
    {
        var items = new List<TResponse>();
        await foreach (var item in scope.ServiceProvider.GetRequiredService<MediatR.ISender>()
            .CreateStream(request, TestContext.Current.CancellationToken))
        {
            items.Add(item);
        }

        return items;
    }

    /// <summary>Same, for commands that return nothing.</summary>
    public Task Send(MediatR.IRequest request) =>
        scope.ServiceProvider.GetRequiredService<MediatR.ISender>().Send(request, TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => scope.DisposeAsync();
}

/// <summary>Shares one database container across all test classes in the "Database" collection.</summary>
[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "Database";
}

/// <summary>
/// A second, separate database container for tests that must own a whole table (e.g. the job queue takes
/// "the oldest due job" of all), so jobs other test classes create can never interfere.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IsolatedDatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "Isolated database";
}

/// <summary>
/// A third database container that holds only what its tests put there: the search index's shape depends on every
/// vector in the table, so tests of how the index behaves must not share it with other tests' made-up vectors.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SearchIndexCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "Search index";
}
