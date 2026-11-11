using System.Net;
using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Web;
using Citra.Application.Features.Documents.AddUrl;
using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.Domain.Users;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Citra.IntegrationTests.Features.Documents;

[Collection(DatabaseCollection.Name)]
public sealed class AddUrlDocumentTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    // C-270: a page's title becomes its name, without invisible characters or line breaks.
    [InlineData("Returns‮ and\n\n  refunds​", "Returns and refunds")]
    [InlineData("​\u0007 \n", "help.acme.com/returns")] // nothing visible: the address stays the name
    public void A_web_pages_title_is_cleaned_before_it_becomes_the_name(string title, string name)
    {
        var document = Document.FromUrl(Guid.NewGuid(), Guid.NewGuid(), new Uri("https://help.acme.com/returns"));

        document.SetTitle(title);

        document.FileName.ShouldBe(name);
    }

    [Fact]
    public async Task Adds_the_page_and_queues_it_for_processing()
    {
        var (tenant, bot) = await SeedBotAsync();

        await using var scope = fixture.OpenScope(tenant.Id);
        var dto = await scope.Send(new AddUrlDocumentCommand(bot.Id, "  https://Help.Acme.com/returns?lang=en#policy  "));

        dto.SourceType.ShouldBe(DocumentSourceType.Url);
        dto.SourceUrl.ShouldBe("https://help.acme.com/returns?lang=en");
        dto.FileName.ShouldBe("help.acme.com/returns");
        dto.Status.ShouldBe(DocumentStatus.Uploaded);

        await using var check = fixture.OpenScope(tenant.Id);
        (await check.Db.ProcessingJobs.SingleAsync(j => j.DocumentId == dto.Id, Ct)).Status
            .ShouldBe(ProcessingJobStatus.Pending);
        (await check.Db.Documents.SingleAsync(d => d.Id == dto.Id, Ct)).StoragePath
            .ShouldBeNull("nothing is downloaded until the Worker fetches the page");
    }

    [Fact]
    public async Task Rejects_the_same_page_twice_in_one_bot()
    {
        var (tenant, bot) = await SeedBotAsync();
        await using var scope = fixture.OpenScope(tenant.Id);
        await scope.Send(new AddUrlDocumentCommand(bot.Id, "https://acme.com/faq"));

        // Same page: different case in the host and a #fragment.
        var error = await Should.ThrowAsync<ValidationException>(
            () => scope.Send(new AddUrlDocumentCommand(bot.Id, "https://ACME.com/faq#shipping")));

        error.Errors["Url"].ShouldBe(["This page is already in this bot."]);
    }

    [Fact]
    public async Task Two_simultaneous_adds_of_the_same_page_store_it_once()
    {
        var (tenant, bot) = await SeedBotAsync();

        async Task<Exception?> AddInOwnScope()
        {
            await using var scope = fixture.OpenScope(tenant.Id);
            try
            {
                await scope.Send(new AddUrlDocumentCommand(bot.Id, "https://acme.com/pricing"));
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => AddInOwnScope()));

        results.Count(r => r is null).ShouldBe(1);
        results.Where(r => r is not null).ShouldAllBe(r => r is ValidationException);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("ftp://acme.com/file.txt")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://localhost/admin")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://intranet/wiki")]
    [InlineData("https://acme.com:8080/")]
    [InlineData("https://user:secret@acme.com/")]
    public async Task Rejects_invalid_or_internal_addresses(string url)
    {
        var (tenant, bot) = await SeedBotAsync();

        await using var scope = fixture.OpenScope(tenant.Id);
        var error = await Should.ThrowAsync<ValidationException>(() => scope.Send(new AddUrlDocumentCommand(bot.Id, url)));

        error.Errors.Keys.ShouldContain("Url");
        (await scope.Db.Documents.AnyAsync(d => d.BotId == bot.Id, Ct)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(UserRole.Viewer)]
    [InlineData(UserRole.Demo)]
    public async Task Read_only_roles_cannot_add_pages(UserRole role)
    {
        var (tenant, bot) = await SeedBotAsync();

        await using var scope = fixture.OpenScope(tenant.Id, role);
        await Should.ThrowAsync<ForbiddenException>(
            () => scope.Send(new AddUrlDocumentCommand(bot.Id, "https://acme.com/")));
    }

    [Fact]
    public async Task Cannot_add_pages_to_another_companys_bot()
    {
        var (_, foreignBot) = await SeedBotAsync();
        var (me, _) = await SeedBotAsync();

        await using var scope = fixture.OpenScope(me.Id);
        await Should.ThrowAsync<NotFoundException>(
            () => scope.Send(new AddUrlDocumentCommand(foreignBot.Id, "https://acme.com/")));
    }

    // ---------- UrlSafety (no database needed) ----------

    [Theory]
    [InlineData("https://acme.com/help")]
    [InlineData("http://docs.acme.co.uk/")]
    [InlineData("https://8.8.8.8/")]
    [InlineData("https://[2606:4700:4700::1111]/")]
    [InlineData("https://xn--mnchen-3ya.de/")]
    public void Allows_public_addresses(string url) => UrlSafety.Check(new Uri(url)).ShouldBeNull();

    [Theory]
    [InlineData("http://2130706433/")]            // 127.0.0.1 written as one number
    [InlineData("http://0x7f.0.0.1/")]            // 127.0.0.1 in hex
    [InlineData("http://0177.0.0.1/")]            // 127.0.0.1 in octal
    [InlineData("http://127.1/")]                 // 127.0.0.1 short form
    [InlineData("http://0.0.0.0/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://172.16.0.1/")]
    [InlineData("http://172.31.255.255/")]
    [InlineData("http://100.64.0.1/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]   // IPv4 hidden in IPv6
    [InlineData("http://[fd00::1]/")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://metadata.google.internal/")]
    [InlineData("http://printer.local/")]
    [InlineData("http://app.localhost/")]
    [InlineData("http://LOCALHOST./")]
    public void Refuses_internal_addresses_in_any_disguise(string url) =>
        UrlSafety.Check(new Uri(url)).ShouldNotBeNull();

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("1.1.1.1", true)]
    [InlineData("172.15.0.1", true)]  // just outside 172.16.0.0/12
    [InlineData("172.32.0.1", true)]
    [InlineData("169.254.169.254", false)]
    [InlineData("192.168.0.10", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("::1", false)]
    [InlineData("64:ff9b::7f00:1", false)]
    [InlineData("2002:7f00:1::", false)]
    [InlineData("2001:0:4136:e378:8000:63bf:3fff:fdd2", false)] // Teredo
    [InlineData("::7f00:1", false)]                           // IPv4-compatible ::127.0.0.1
    [InlineData("2001:4860:4860::8888", true)]                // a public 2001:4860:: address is not Teredo
    [InlineData("2606:4700:4700::1111", true)]
    public void Knows_which_resolved_addresses_are_public(string ip, bool isPublic) =>
        UrlSafety.IsPublicAddress(IPAddress.Parse(ip)).ShouldBe(isPublic);

    // ---------- helpers ----------

    private async Task<(Tenant Tenant, Bot Bot)> SeedBotAsync()
    {
        var tenant = Tenant.Create("Url test company");
        tenant.ChangeSlug($"url-{Guid.NewGuid():N}");
        var bot = Bot.Create(tenant.Id, "Support");

        await using var system = fixture.OpenScope(tenantId: null);
        system.Db.AddRange(tenant, bot);
        await system.Db.SaveChangesAsync(Ct);
        return (tenant, bot);
    }
}
