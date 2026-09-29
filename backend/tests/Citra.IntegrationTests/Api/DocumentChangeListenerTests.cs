using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Citra.Api.Hubs;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Storage;
using Citra.Application.Features.Documents;
using Citra.Application.Features.Documents.Processing;
using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Citra.Infrastructure.Persistence;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Citra.IntegrationTests.Api;

/// <summary>
/// C-149: document changes made anywhere (here: straight in the database, and by the real processing pipeline, as
/// the Worker does) reach the browsers of the document's company through the real API's listener and hub —
/// and keep coming after the listener's database connection is lost.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DocumentChangeListenerTests(DatabaseFixture database) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ApiFactory _api = null!;

    public async ValueTask InitializeAsync()
    {
        _api = new ApiFactory(database);
        _ = _api.Server; // starts the API, and with it the listener
        await WaitForListenerAsync();
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Added_changed_and_deleted_documents_reach_their_company_only()
    {
        var user = await _api.SignUpAsync(Ct);
        var other = await _api.SignUpAsync(Ct);
        await using var browser = await ConnectAsync(user);
        await using var otherBrowser = await ConnectAsync(other);
        var bot = await SeedBotAsync(user.TenantId);

        var document = await SeedDocumentAsync(user.TenantId, bot.Id);
        (await browser.NextChangeAsync(document.Id)).GetProperty("status").GetString().ShouldBe("Uploaded");

        await using (var scope = database.OpenScope(tenantId: null))
        {
            await scope.Db.Database.ExecuteSqlAsync($"UPDATE documents SET status = 'Extracting' WHERE id = {document.Id}", Ct);
        }

        var changed = await browser.NextChangeAsync(document.Id);
        changed.GetProperty("status").GetString().ShouldBe("Extracting");
        changed.GetProperty("botId").GetGuid().ShouldBe(bot.Id);

        await using (var scope = database.OpenScope(tenantId: null))
        {
            await scope.Db.Documents.IgnoreQueryFilters([QueryFilters.Tenant]).Where(d => d.Id == document.Id).ExecuteDeleteAsync(Ct);
        }

        var removed = await browser.NextRemovalAsync(document.Id);
        removed.GetProperty("botId").GetGuid().ShouldBe(bot.Id);

        await Task.Delay(300, Ct);
        otherBrowser.Received.ShouldBeEmpty("another company never hears about these documents");
    }

    [Fact]
    public async Task The_workers_progress_streams_to_the_page_until_Ready()
    {
        var user = await _api.SignUpAsync(Ct);
        await using var browser = await ConnectAsync(user);
        var bot = await SeedBotAsync(user.TenantId);
        var document = await SeedDocumentAsync(user.TenantId, bot.Id, storedText: "Employees get 25 days of paid vacation.");
        await browser.NextChangeAsync(document.Id); // added

        // Exactly what the Worker does for this job: every step commits its own status.
        var job = ProcessingJob.For(document.Id);
        job.Start(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(15));
        await using (var scope = database.OpenScope(tenantId: null))
        {
            scope.Db.ProcessingJobs.Add(job);
            await scope.Db.SaveChangesAsync(Ct);
        }

        await using (var worker = database.OpenScope(tenantId: null))
        {
            var tracked = await worker.Db.ProcessingJobs.SingleAsync(j => j.Id == job.Id, Ct);
            await worker.Services.GetRequiredService<DocumentPipeline>().RunAsync(tracked, Ct);
        }

        var statuses = new List<string>();
        JsonElement last;
        do
        {
            last = await browser.NextChangeAsync(document.Id);
            statuses.Add(last.GetProperty("status").GetString()!);
        }
        while (statuses[^1] != "Ready");

        // Each update shows the document as it is when sent, so quick steps may merge — but never go backwards.
        string[] order = ["Extracting", "Chunking", "Embedding", "Ready"];
        statuses.Select(s => Array.IndexOf(order, s)).ShouldBeInOrder(SortDirection.Ascending);
        statuses.ShouldAllBe(s => order.Contains(s));
        last.GetProperty("chunkCount").GetInt32().ShouldBe(1);
        last.GetProperty("pageCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Updates_keep_coming_after_the_database_connection_is_lost()
    {
        var user = await _api.SignUpAsync(Ct);
        await using var browser = await ConnectAsync(user);
        var bot = await SeedBotAsync(user.TenantId);

        // E.g. the database restarted: the listener's connection is cut.
        var cut = await ListenerPidsAsync();
        cut.ShouldNotBeEmpty();
        await using (var scope = database.OpenScope(tenantId: null))
        {
            foreach (var pid in cut)
            {
                await scope.Db.Database.ExecuteSqlAsync($"SELECT pg_terminate_backend({pid})", Ct);
            }
        }

        await WaitForListenerAsync(notOneOf: cut); // a NEW connection, after the reconnect delay

        var document = await SeedDocumentAsync(user.TenantId, bot.Id);
        (await browser.NextChangeAsync(document.Id)).GetProperty("status").GetString().ShouldBe("Uploaded");
    }

    // ---------- helpers ----------

    /// <summary>
    /// Until the API's listener has run LISTEN on a database connection (changes before that are not sent) —
    /// optionally a new one, not one of <paramref name="notOneOf"/> (a cut connection can stay listed a moment).
    /// </summary>
    private async Task WaitForListenerAsync(IReadOnlyCollection<int>? notOneOf = null)
    {
        var waited = Stopwatch.StartNew();
        while (!(await ListenerPidsAsync()).Any(pid => notOneOf?.Contains(pid) != true))
        {
            waited.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(20), "the API never started listening");
            await Task.Delay(100, Ct);
        }
    }

    /// <summary>The database processes of connections whose last command was the listener's LISTEN.</summary>
    private async Task<List<int>> ListenerPidsAsync()
    {
        await using var scope = database.OpenScope(tenantId: null);
        return await scope.Db.Database
            .SqlQueryRaw<int>("SELECT pid AS \"Value\" FROM pg_stat_activity WHERE query LIKE 'LISTEN document_changes%'")
            .ToListAsync(Ct);
    }

    /// <summary>
    /// A documents page of this user, connected and already in its company's group (checked with a probe message:
    /// the server adds a connection to its group just after the connection is established).
    /// </summary>
    private async Task<Browser> ConnectAsync(SignedUpUser user)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_api.Server.BaseAddress, DocumentsHub.Path), options =>
            {
                options.HttpMessageHandlerFactory = _ => _api.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(user.AccessToken);
            })
            .Build();
        var browser = new Browser(connection);
        await connection.StartAsync(Ct);

        var probe = Guid.NewGuid();
        var hub = _api.Services.GetRequiredService<IHubContext<DocumentsHub, IDocumentsClient>>();
        var waited = Stopwatch.StartNew();
        while (!browser.Received.Any(r => r.Json.GetProperty("id").GetGuid() == probe))
        {
            waited.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10), "never joined its company's group");
            await hub.Clients.Group(DocumentsHub.TenantGroup(user.TenantId)).DocumentRemoved(new RemovedDocument(probe, Guid.Empty));
            await Task.Delay(100, Ct);
        }

        browser.Received.Clear();
        return browser;
    }

    private async Task<Bot> SeedBotAsync(Guid tenantId)
    {
        var bot = Bot.Create(tenantId, $"Support {Guid.NewGuid():N}");
        await using var scope = database.OpenScope(tenantId: null);
        scope.Db.Bots.Add(bot);
        await scope.Db.SaveChangesAsync(Ct);
        return bot;
    }

    private async Task<Document> SeedDocumentAsync(Guid tenantId, Guid botId, string storedText = "Text.")
    {
        var id = Guid.CreateVersion7();
        var path = StoragePaths.OriginalFile(tenantId, botId, id, "notes.txt");
        var bytes = Encoding.UTF8.GetBytes(storedText);
        var document = Document.FromUpload(
            tenantId, botId, "notes.txt", "text/plain", bytes.Length, DocumentSourceType.Text, path, new string('a', 64), id);

        await using var scope = database.OpenScope(tenantId: null);
        await scope.Services.GetRequiredService<IFileStorage>().SaveAsync(path, new MemoryStream(bytes), Ct);
        scope.Db.Documents.Add(document);
        await scope.Db.SaveChangesAsync(Ct);
        return document;
    }

    /// <summary>A connected documents page: records every message it gets.</summary>
    private sealed class Browser : IAsyncDisposable
    {
        private readonly HubConnection _connection;

        public Browser(HubConnection connection)
        {
            _connection = connection;
            connection.On<JsonElement>(nameof(IDocumentsClient.DocumentChanged), json => Received.Enqueue((nameof(IDocumentsClient.DocumentChanged), json)));
            connection.On<JsonElement>(nameof(IDocumentsClient.DocumentRemoved), json => Received.Enqueue((nameof(IDocumentsClient.DocumentRemoved), json)));
        }

        public ConcurrentQueue<(string Kind, JsonElement Json)> Received { get; } = new();

        public Task<JsonElement> NextChangeAsync(Guid documentId) => NextAsync(nameof(IDocumentsClient.DocumentChanged), documentId);

        public Task<JsonElement> NextRemovalAsync(Guid documentId) => NextAsync(nameof(IDocumentsClient.DocumentRemoved), documentId);

        public ValueTask DisposeAsync() => _connection.DisposeAsync();

        /// <summary>Takes the oldest message of this kind about this document (waits up to 10 s).</summary>
        private async Task<JsonElement> NextAsync(string kind, Guid documentId)
        {
            var waited = Stopwatch.StartNew();
            while (true)
            {
                if (Received.TryPeek(out var next))
                {
                    Received.TryDequeue(out _);
                    if (next.Kind == kind && next.Json.GetProperty("id").GetGuid() == documentId)
                    {
                        return next.Json;
                    }

                    throw new ShouldAssertException($"Expected {kind} for {documentId}, got {next.Kind}: {next.Json}");
                }

                waited.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10), $"no {kind} for document {documentId} arrived");
                await Task.Delay(20);
            }
        }
    }
}
