using System.Collections.Concurrent;
using System.Diagnostics;
using Citra.Application.Common.Persistence;
using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.Infrastructure.Persistence;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Citra.IntegrationTests.Persistence;

/// <summary>
/// The documents table's trigger sends a "document_changes" notification for exactly the changes the
/// documents page shows — on commit only — with the document, its company and its bot.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DocumentChangeTriggerTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ConcurrentQueue<DocumentChangeNotification> _received = new();
    private NpgsqlConnection _listener = null!;
    private CancellationTokenSource _stopListening = null!;
    private Task _listening = null!;

    public async ValueTask InitializeAsync()
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        _listener = await scope.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(Ct);
        _listener.Notification += (_, e) => _received.Enqueue(DocumentChangeNotification.Parse(e.Payload)!);
        await using (var listen = new NpgsqlCommand($"LISTEN {DocumentChangeNotification.Channel}", _listener))
        {
            await listen.ExecuteNonQueryAsync(Ct);
        }

        _stopListening = new CancellationTokenSource();
        _listening = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    await _listener.WaitAsync(_stopListening.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Test over.
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _stopListening.CancelAsync();
        await _listening;
        await _listener.DisposeAsync();
        _stopListening.Dispose();
    }

    [Fact]
    public async Task Adding_a_document_notifies_with_its_company_and_bot()
    {
        var document = await SeedAsync();

        var change = (await ReceivedForAsync(document.Id)).ShouldHaveSingleItem();

        change.ShouldBe(new DocumentChangeNotification(document.Id, document.TenantId, document.BotId, Deleted: false));
    }

    [Theory]
    [InlineData("status = 'Extracting'")]
    [InlineData("error_message = 'Broken'")]
    [InlineData("page_count = 7")]
    [InlineData("chunk_count = 12")]
    [InlineData("file_name = 'Renamed.pdf'")]
    [InlineData("processed_at = now()")]
    public async Task Every_change_the_page_shows_notifies(string change)
    {
        var document = await SeedAsync();
        await ReceivedForAsync(document.Id); // the insert

        await UpdateAsync(document.Id, change);

        (await ReceivedForAsync(document.Id)).ShouldHaveSingleItem().Deleted.ShouldBeFalse();
    }

    [Theory]
    [InlineData("updated_at = now() + interval '1 minute'")]
    [InlineData("content_hash = 'different'")]
    [InlineData("status = status")]
    public async Task Changes_the_page_does_not_show_notify_nothing(string change)
    {
        var document = await SeedAsync();
        await ReceivedForAsync(document.Id);

        await UpdateAsync(document.Id, change);

        (await ReceivedForAsync(document.Id, expectNone: true)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_rolled_back_change_notifies_nothing()
    {
        var document = await SeedAsync();
        await ReceivedForAsync(document.Id);

        await using (var scope = fixture.OpenScope(tenantId: null))
        {
            await using var transaction = await scope.Db.Database.BeginTransactionAsync(Ct);
            await scope.Db.Database.ExecuteSqlAsync($"UPDATE documents SET status = 'Failed' WHERE id = {document.Id}", Ct);
            await transaction.RollbackAsync(Ct);
        }

        (await ReceivedForAsync(document.Id, expectNone: true)).ShouldBeEmpty("never announced: it never happened");
    }

    [Fact]
    public async Task A_change_is_announced_only_when_committed()
    {
        var document = await SeedAsync();
        await ReceivedForAsync(document.Id);

        await using var scope = fixture.OpenScope(tenantId: null);
        await using var transaction = await scope.Db.Database.BeginTransactionAsync(Ct);
        await scope.Db.Database.ExecuteSqlAsync($"UPDATE documents SET status = 'Extracting' WHERE id = {document.Id}", Ct);
        (await ReceivedForAsync(document.Id, expectNone: true)).ShouldBeEmpty("not yet committed");

        await transaction.CommitAsync(Ct);
        (await ReceivedForAsync(document.Id)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Deleting_a_document_or_its_whole_bot_notifies_removal()
    {
        var first = await SeedAsync();
        var second = await SeedAsync(sameBotAs: first);
        var alone = await SeedAsync();
        await ReceivedForAsync(first.Id);
        await ReceivedForAsync(second.Id);
        await ReceivedForAsync(alone.Id);

        await using (var scope = fixture.OpenScope(tenantId: null))
        {
            await scope.Db.Documents.IgnoreQueryFilters([QueryFilters.Tenant]).Where(d => d.Id == alone.Id).ExecuteDeleteAsync(Ct);
            await scope.Db.Bots.IgnoreQueryFilters([QueryFilters.Tenant]).Where(b => b.Id == first.BotId).ExecuteDeleteAsync(Ct); // cascades
        }

        foreach (var document in new[] { alone, first, second })
        {
            (await ReceivedForAsync(document.Id)).ShouldHaveSingleItem()
                .ShouldBe(new DocumentChangeNotification(document.Id, document.TenantId, document.BotId, Deleted: true));
        }
    }

    [Theory]
    [InlineData("""{"id":"not-a-guid","tenant_id":"00000000-0000-0000-0000-000000000001","bot_id":"00000000-0000-0000-0000-000000000002","deleted":false}""")]
    [InlineData("""{"id":"00000000-0000-0000-0000-000000000001"}""")]
    [InlineData("""{"id":"00000000-0000-0000-0000-000000000001","tenant_id":"00000000-0000-0000-0000-000000000002","bot_id":"00000000-0000-0000-0000-000000000003"}""")]
    [InlineData("""{"id":"00000000-0000-0000-0000-000000000001","tenant_id":"00000000-0000-0000-0000-000000000002","bot_id":"00000000-0000-0000-0000-000000000003","deleted":"yes"}""")]
    [InlineData("not json")]
    [InlineData("")]
    public void A_payload_that_is_not_the_triggers_is_rejected(string payload)
    {
        DocumentChangeNotification.Parse(payload).ShouldBeNull();
    }

    // ---------- helpers ----------

    /// <summary>
    /// The notifications for one document: waits until one arrives (up to 5 s), or — when none is expected —
    /// gives a wrongly sent one half a second to show up. Removes what it returns.
    /// </summary>
    private async Task<List<DocumentChangeNotification>> ReceivedForAsync(Guid documentId, bool expectNone = false)
    {
        var waited = Stopwatch.StartNew();
        var limit = expectNone ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromSeconds(5);
        while (waited.Elapsed < limit && !_received.Any(n => n.DocumentId == documentId))
        {
            await Task.Delay(20, Ct);
        }

        await Task.Delay(100, Ct); // a second one sent by mistake would be right behind the first
        var mine = _received.Where(n => n.DocumentId == documentId).ToList();
        var others = _received.Where(n => n.DocumentId != documentId).ToList();
        _received.Clear();
        foreach (var other in others)
        {
            _received.Enqueue(other);
        }

        if (!expectNone)
        {
            mine.ShouldNotBeEmpty($"no notification for document {documentId}");
        }

        return mine;
    }

    private async Task UpdateAsync(Guid documentId, string change)
    {
        await using var scope = fixture.OpenScope(tenantId: null);

        // The SET part is one of this class's own fixed test values (never input); the id is a real parameter.
#pragma warning disable EF1002
        await scope.Db.Database.ExecuteSqlRawAsync($"UPDATE documents SET {change} WHERE id = @p0", [documentId], Ct);
#pragma warning restore EF1002
    }

    private async Task<Document> SeedAsync(Document? sameBotAs = null)
    {
        var entities = new List<object>();
        Guid tenantId, botId;
        if (sameBotAs is null)
        {
            var tenant = Tenant.Create("Notify company");
            tenant.ChangeSlug($"notify-{Guid.NewGuid():N}");
            var bot = Bot.Create(tenant.Id, "Support");
            entities.AddRange([tenant, bot]);
            (tenantId, botId) = (tenant.Id, bot.Id);
        }
        else
        {
            (tenantId, botId) = (sameBotAs.TenantId, sameBotAs.BotId);
        }

        var document = Document.FromUpload(
            tenantId, botId, "Handbook.pdf", "application/pdf", 100, DocumentSourceType.Pdf,
            $"{tenantId}/{botId}/{Guid.NewGuid():N}/original.pdf", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
        entities.Add(document);

        await using var scope = fixture.OpenScope(tenantId: null);
        scope.Db.AddRange(entities);
        await scope.Db.SaveChangesAsync(Ct);
        return document;
    }
}
