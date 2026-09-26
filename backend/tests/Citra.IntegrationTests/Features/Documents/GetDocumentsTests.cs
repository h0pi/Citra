using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Documents.List;
using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.Domain.Users;
using Citra.IntegrationTests.Infrastructure;

namespace Citra.IntegrationTests.Features.Documents;

[Collection(DatabaseCollection.Name)]
public sealed class GetDocumentsTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lists_a_bots_documents_newest_first_with_totals()
    {
        var (tenant, bot) = await SeedBotAsync();
        await SeedDocumentsAsync(tenant, bot, "first.pdf", "second.pdf", "third.pdf");

        await using var scope = fixture.OpenScope(tenant.Id);
        var page = await scope.Send(new GetDocumentsQuery(bot.Id));

        page.Items.Select(d => d.FileName).ShouldBe(["third.pdf", "second.pdf", "first.pdf"]);
        page.TotalCount.ShouldBe(3);
        page.Page.ShouldBe(1);
        page.PageSize.ShouldBe(20);
        page.TotalPages.ShouldBe(1);
    }

    [Fact]
    public async Task Pages_through_the_list_without_repeating_or_skipping()
    {
        var (tenant, bot) = await SeedBotAsync();
        var names = Enumerable.Range(1, 7).Select(i => $"doc-{i:00}.pdf").ToArray();
        await SeedDocumentsAsync(tenant, bot, names);

        await using var scope = fixture.OpenScope(tenant.Id);
        var pages = new List<string>();
        for (var p = 1; p <= 3; p++)
        {
            var page = await scope.Send(new GetDocumentsQuery(bot.Id, Page: p, PageSize: 3));
            page.TotalCount.ShouldBe(7);
            page.TotalPages.ShouldBe(3);
            pages.AddRange(page.Items.Select(d => d.FileName));
        }

        pages.ShouldBe(names.Reverse());
        (await scope.Send(new GetDocumentsQuery(bot.Id, Page: 4, PageSize: 3))).Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(DocumentStatusFilter.Queued, new[] { "queued.pdf" })]
    [InlineData(DocumentStatusFilter.Processing, new[] { "embedding.pdf", "extracting.pdf" })]
    [InlineData(DocumentStatusFilter.Ready, new[] { "ready.pdf" })]
    [InlineData(DocumentStatusFilter.Failed, new[] { "failed.pdf" })]
    public async Task Filters_by_status_group(DocumentStatusFilter filter, string[] expected)
    {
        var (tenant, bot) = await SeedBotAsync();
        var now = DateTimeOffset.UtcNow;
        await SeedDocumentsAsync(tenant, bot, ("queued.pdf", _ => { }),
            ("extracting.pdf", d => d.StartExtracting()),
            ("embedding.pdf", d => { d.StartExtracting(); d.StartChunking(); d.StartEmbedding(); }),
            ("ready.pdf", d => { d.StartExtracting(); d.StartChunking(); d.StartEmbedding(); d.MarkReady(3, 12, now); }),
            ("failed.pdf", d => { d.StartExtracting(); d.MarkFailed("Scanned PDF", now); }));

        await using var scope = fixture.OpenScope(tenant.Id);
        var page = await scope.Send(new GetDocumentsQuery(bot.Id, Status: filter));

        page.Items.Select(d => d.FileName).ShouldBe(expected);
        page.TotalCount.ShouldBe(expected.Length);
    }

    [Theory]
    [InlineData("handbook", new[] { "Employee Handbook.pdf", "handbook-2025.docx" })]
    [InlineData("%", new[] { "100% refund.txt" })]
    [InlineData("HANDBOOK 2025", new string[0])]
    [InlineData("  pricing ", new[] { "Pricing.md" })]
    [InlineData("100%", new[] { "100% refund.txt" })]
    [InlineData("_", new[] { "notes_v2.txt" })] // "_" and "%" match literally, not as SQL wildcards
    public async Task Searches_file_names_ignoring_case(string search, string[] expected)
    {
        var (tenant, bot) = await SeedBotAsync();
        await SeedDocumentsAsync(tenant, bot, "Employee Handbook.pdf", "Pricing.md", "100% refund.txt", "handbook-2025.docx", "notes_v2.txt");

        await using var scope = fixture.OpenScope(tenant.Id);
        var page = await scope.Send(new GetDocumentsQuery(bot.Id, Search: search));

        page.Items.Select(d => d.FileName).ShouldBe(expected, ignoreOrder: true);
    }

    [Fact]
    public async Task Search_and_status_filter_combine()
    {
        var (tenant, bot) = await SeedBotAsync();
        var now = DateTimeOffset.UtcNow;
        await SeedDocumentsAsync(tenant, bot,
            ("policy-a.pdf", d => { d.StartExtracting(); d.MarkFailed("x", now); }),
            ("policy-b.pdf", _ => { }),
            ("other.pdf", d => { d.StartExtracting(); d.MarkFailed("x", now); }));

        await using var scope = fixture.OpenScope(tenant.Id);
        var page = await scope.Send(new GetDocumentsQuery(bot.Id, Status: DocumentStatusFilter.Failed, Search: "policy"));

        page.Items.Select(d => d.FileName).ShouldBe(["policy-a.pdf"]);
    }

    [Fact]
    public async Task Shows_only_the_requested_bots_documents()
    {
        var (tenant, bot) = await SeedBotAsync();
        var otherBot = Bot.Create(tenant.Id, "Other");
        await SaveAsSystemAsync(otherBot);
        await SeedDocumentsAsync(tenant, bot, "mine.pdf");
        await SeedDocumentsAsync(tenant, otherBot, "not-mine.pdf");

        await using var scope = fixture.OpenScope(tenant.Id);
        (await scope.Send(new GetDocumentsQuery(bot.Id))).Items.Select(d => d.FileName).ShouldBe(["mine.pdf"]);
    }

    [Fact]
    public async Task A_bot_without_documents_returns_an_empty_page()
    {
        var (tenant, bot) = await SeedBotAsync();

        await using var scope = fixture.OpenScope(tenant.Id);
        var page = await scope.Send(new GetDocumentsQuery(bot.Id));

        page.Items.ShouldBeEmpty();
        page.TotalCount.ShouldBe(0);
        page.TotalPages.ShouldBe(0);
    }

    [Fact]
    public async Task Another_companys_bot_is_not_found()
    {
        var (theirs, theirBot) = await SeedBotAsync();
        await SeedDocumentsAsync(theirs, theirBot, "secret.pdf");
        var (me, _) = await SeedBotAsync();

        await using var scope = fixture.OpenScope(me.Id);
        await Should.ThrowAsync<NotFoundException>(() => scope.Send(new GetDocumentsQuery(theirBot.Id)));
    }

    [Theory]
    [InlineData(UserRole.Viewer)]
    [InlineData(UserRole.Demo)]
    public async Task Read_only_roles_can_view_documents(UserRole role)
    {
        var (tenant, bot) = await SeedBotAsync();
        await SeedDocumentsAsync(tenant, bot, "visible.pdf");

        await using var scope = fixture.OpenScope(tenant.Id, role);
        (await scope.Send(new GetDocumentsQuery(bot.Id))).TotalCount.ShouldBe(1);
    }

    [Theory]
    [InlineData(0, 20, "Page")]
    [InlineData(1, 0, "PageSize")]
    [InlineData(1, 101, "PageSize")]
    public async Task Rejects_invalid_paging(int page, int pageSize, string field)
    {
        var (tenant, bot) = await SeedBotAsync();

        await using var scope = fixture.OpenScope(tenant.Id);
        var error = await Should.ThrowAsync<ValidationException>(
            () => scope.Send(new GetDocumentsQuery(bot.Id, page, pageSize)));
        error.Errors.Keys.ShouldContain(field);
    }

    // ---------- helpers ----------

    private async Task<(Tenant Tenant, Bot Bot)> SeedBotAsync()
    {
        var tenant = Tenant.Create("List docs company");
        tenant.ChangeSlug($"docs-{Guid.NewGuid():N}");
        var bot = Bot.Create(tenant.Id, "Support");
        await SaveAsSystemAsync(tenant, bot);
        return (tenant, bot);
    }

    private Task SeedDocumentsAsync(Tenant tenant, Bot bot, params string[] names) =>
        SeedDocumentsAsync(tenant, bot, names.Select(n => (n, (Action<Document>)(_ => { }))).ToArray());

    /// <summary>Saves the documents one by one, in order, so each is newer than the one before.</summary>
    private async Task SeedDocumentsAsync(Tenant tenant, Bot bot, params (string Name, Action<Document> Prepare)[] docs)
    {
        foreach (var (name, prepare) in docs)
        {
            var document = Document.FromUpload(
                tenant.Id, bot.Id, name, "application/pdf", 100, DocumentSourceType.Pdf,
                $"{tenant.Id}/{bot.Id}/{Guid.NewGuid():N}/original.pdf",
                Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
            prepare(document);
            await SaveAsSystemAsync(document);
        }
    }

    private async Task SaveAsSystemAsync(params object[] entities)
    {
        await using var system = fixture.OpenScope(tenantId: null);
        system.Db.AddRange(entities);
        await system.Db.SaveChangesAsync(Ct);
    }
}
