using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Documents.Chunks;
using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.Domain.Users;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Citra.IntegrationTests.Features.Documents;

/// <summary>C-161: a document's chunks, in reading order and paged, for its company only.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class GetDocumentChunksTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lists_the_chunks_in_reading_order_with_what_the_page_shows()
    {
        var (tenant, document) = await SeedAsync(chunkCount: 3);

        await using var scope = fixture.OpenScope(tenant.Id, UserRole.Viewer); // viewing is for every role
        var result = await scope.Send(new GetDocumentChunksQuery(document.Id));

        result.TotalCount.ShouldBe(3);
        result.Items.Select(c => c.Index).ShouldBe([0, 1, 2]);
        var first = result.Items[0];
        first.Content.ShouldBe("Chunk 0: employees get 25 days of paid vacation.");
        first.Heading.ShouldBe("Vacation");
        first.PageNumber.ShouldBe(1);
        first.TokenCount.ShouldBe(10);
        first.CharStart.ShouldBe(0);
        first.CharEnd.ShouldBe(first.Content.Length);
        result.Items[2].PageNumber.ShouldBe(3);
    }

    [Fact]
    public async Task Keeps_reading_order_however_the_database_reads_the_rows()
    {
        // The chunks were saved last-first. Reading the table row by row (no index) returns them in that saved
        // order, so only an explicit ORDER BY gives reading order — the index happens to hide that otherwise.
        var (tenant, document) = await SeedAsync(chunkCount: 4);

        await using var scope = fixture.OpenScope(tenant.Id);
        await scope.Db.Database.OpenConnectionAsync(Ct);
        await scope.Db.Database.ExecuteSqlRawAsync("SET enable_indexscan = off; SET enable_bitmapscan = off", Ct);
        try
        {
            var result = await scope.Send(new GetDocumentChunksQuery(document.Id));

            result.Items.Select(c => c.Index).ShouldBe([0, 1, 2, 3]);
        }
        finally
        {
            await scope.Db.Database.ExecuteSqlRawAsync("RESET enable_indexscan; RESET enable_bitmapscan", Ct);
            await scope.Db.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task Pages_through_many_chunks()
    {
        var (tenant, document) = await SeedAsync(chunkCount: 5);

        await using var scope = fixture.OpenScope(tenant.Id);
        var second = await scope.Send(new GetDocumentChunksQuery(document.Id, Page: 2, PageSize: 2));
        var last = await scope.Send(new GetDocumentChunksQuery(document.Id, Page: 3, PageSize: 2));

        second.Items.Select(c => c.Index).ShouldBe([2, 3]);
        second.TotalCount.ShouldBe(5);
        second.TotalPages.ShouldBe(3);
        last.Items.Select(c => c.Index).ShouldBe([4]);
    }

    [Fact]
    public async Task A_document_not_processed_yet_has_no_chunks()
    {
        var (tenant, document) = await SeedAsync(chunkCount: 0);

        await using var scope = fixture.OpenScope(tenant.Id);
        var result = await scope.Send(new GetDocumentChunksQuery(document.Id));

        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Another_companys_chunks_stay_hidden()
    {
        var (_, foreign) = await SeedAsync(chunkCount: 2);
        var (me, _) = await SeedAsync(chunkCount: 0);

        await using var scope = fixture.OpenScope(me.Id);

        await Should.ThrowAsync<NotFoundException>(() => scope.Send(new GetDocumentChunksQuery(foreign.Id)));
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Refuses_impossible_pages(int page, int pageSize)
    {
        var (tenant, document) = await SeedAsync(chunkCount: 1);

        await using var scope = fixture.OpenScope(tenant.Id);

        await Should.ThrowAsync<ValidationException>(() =>
            scope.Send(new GetDocumentChunksQuery(document.Id, page, pageSize)));
    }

    [Fact]
    public async Task The_api_serves_them_at_documents_id_chunks()
    {
        await using var api = new ApiFactory(fixture);
        var user = await api.SignUpAsync(Ct);
        var document = await SeedDocumentAsync(user.TenantId, chunkCount: 3);
        using var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);

        var response = await client.GetAsync($"/api/documents/{document.Id}/chunks?page=2&pageSize=2", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("totalCount").GetInt32().ShouldBe(3);
        var chunk = body.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        chunk.GetProperty("index").GetInt32().ShouldBe(2);
        chunk.GetProperty("content").GetString().ShouldStartWith("Chunk 2:");
        chunk.TryGetProperty("embedding", out _).ShouldBeFalse("vectors are never sent to the browser");

        (await client.GetAsync($"/api/documents/{Guid.NewGuid()}/chunks", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        client.DefaultRequestHeaders.Authorization = null;
        (await client.GetAsync($"/api/documents/{document.Id}/chunks", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------- helpers ----------

    private async Task<(Tenant Tenant, Document Document)> SeedAsync(int chunkCount)
    {
        var tenant = Tenant.Create("Chunks company");
        tenant.ChangeSlug($"chunks-{Guid.NewGuid():N}");
        await using (var scope = fixture.OpenScope(tenantId: null))
        {
            scope.Db.Tenants.Add(tenant);
            await scope.Db.SaveChangesAsync(Ct);
        }

        return (tenant, await SeedDocumentAsync(tenant.Id, chunkCount));
    }

    /// <summary>A PDF of one page per chunk, Ready, in a new bot of the company; chunks saved out of order.</summary>
    private async Task<Document> SeedDocumentAsync(Guid tenantId, int chunkCount)
    {
        var bot = Bot.Create(tenantId, "Support");
        var document = Document.FromUpload(
            tenantId, bot.Id, "Handbook.pdf", "application/pdf", 100, DocumentSourceType.Pdf,
            $"{tenantId}/{bot.Id}/{Guid.NewGuid():N}/original.pdf", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
        var chunks = Enumerable.Range(0, chunkCount)
            .Reverse()
            .Select(i =>
            {
                var content = $"Chunk {i}: employees get 25 days of paid vacation.";
                var chunk = Chunk.Create(document.Id, bot.Id, i, content, "Vacation", i + 1, 0, content.Length, 10);
                chunk.SetEmbedding(FakeEmbeddingService.VectorFor(content));
                return chunk;
            })
            .ToList();

        await using var scope = fixture.OpenScope(tenantId: null);
        scope.Db.AddRange(bot, document);
        scope.Db.Chunks.AddRange(chunks);
        await scope.Db.SaveChangesAsync(Ct);
        return document;
    }
}
