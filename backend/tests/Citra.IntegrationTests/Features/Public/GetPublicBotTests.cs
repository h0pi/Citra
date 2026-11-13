using System.Net;
using System.Net.Http.Json;
using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Public;
using Citra.Domain.Bots;
using Citra.Domain.Tenants;
using Citra.IntegrationTests.Infrastructure;

namespace Citra.IntegrationTests.Features.Public;

/// <summary>The widget loads a public bot by company and bot slug, without a login.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class GetPublicBotTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Finds_a_public_bot_by_company_and_bot_slug_without_a_user()
    {
        var tenant = await SeedAsync(isPublic: true);

        await using var scope = fixture.OpenScope(tenantId: null);
        var bot = await scope.Send(new GetPublicBotQuery(tenant.Slug, "support"));

        bot.Name.ShouldBe("Support");
        bot.WelcomeMessage.ShouldBe("Hi! Ask me anything.");
        bot.WidgetColor.ShouldBe("#10B981");
        bot.StarterQuestions.ShouldBe(["Vacation?"]);
    }

    [Fact]
    public async Task A_private_bot_or_another_companys_bot_is_not_found()
    {
        var hidden = await SeedAsync(isPublic: false);
        var other = await SeedAsync(isPublic: true);

        await using var scope = fixture.OpenScope(tenantId: null);
        await Should.ThrowAsync<NotFoundException>(() => scope.Send(new GetPublicBotQuery(hidden.Slug, "support")));
        await Should.ThrowAsync<NotFoundException>(() => scope.Send(new GetPublicBotQuery(other.Slug, "sales")));
        await Should.ThrowAsync<NotFoundException>(() => scope.Send(new GetPublicBotQuery("no-such-company", "support")));
    }

    [Fact]
    public async Task The_endpoint_needs_no_login()
    {
        var tenant = await SeedAsync(isPublic: true);
        await using var api = new ApiFactory(fixture);
        var client = api.CreateClient();

        var bot = await client.GetFromJsonAsync<PublicBotDto>($"/api/public/bots/{tenant.Slug}/support", Ct);
        using var missing = await client.GetAsync($"/api/public/bots/{tenant.Slug}/nope", Ct);

        bot!.Name.ShouldBe("Support");
        bot.StarterQuestions.ShouldBe(["Vacation?"]);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("https://acme.com", true)]
    [InlineData("https://shop.ACME.com:8443", true)]
    [InlineData("http://localhost:5173", true)]
    [InlineData("https://notacme.com", false)]
    [InlineData("https://acme.com.evil.test", false)]
    [InlineData(null, false)]
    public async Task Only_the_bots_websites_may_use_it(string? origin, bool allowed)
    {
        var tenant = await SeedAsync(isPublic: true, allowedDomains: ["acme.com", "localhost"]);

        await using var scope = fixture.OpenScope(tenantId: null);
        var load = () => scope.Send(new GetPublicBotQuery(tenant.Slug, "support", origin));

        if (allowed)
        {
            (await load()).Name.ShouldBe("Support");
        }
        else
        {
            await Should.ThrowAsync<ForbiddenException>(load);
        }
    }

    [Fact]
    public async Task A_bot_without_a_list_of_websites_works_everywhere()
    {
        var tenant = await SeedAsync(isPublic: true);

        await using var scope = fixture.OpenScope(tenantId: null);
        (await scope.Send(new GetPublicBotQuery(tenant.Slug, "support", "https://anywhere.test"))).Name.ShouldBe("Support");
    }

    [Fact]
    public async Task Any_website_may_call_the_endpoint_and_the_bot_refuses_others()
    {
        var tenant = await SeedAsync(isPublic: true, allowedDomains: ["acme.com"]);
        await using var api = new ApiFactory(fixture);
        var client = api.CreateClient();
        var address = $"/api/public/bots/{tenant.Slug}/support";

        using var preflight = new HttpRequestMessage(HttpMethod.Options, address + "/chat");
        preflight.Headers.Add("Origin", "https://evil.test");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type");
        using var preflightResponse = await client.SendAsync(preflight, Ct);

        using var fromAcme = new HttpRequestMessage(HttpMethod.Get, address);
        fromAcme.Headers.Add("Origin", "https://acme.com");
        using var acmeResponse = await client.SendAsync(fromAcme, Ct);

        using var fromEvil = new HttpRequestMessage(HttpMethod.Get, address);
        fromEvil.Headers.Add("Origin", "https://evil.test");
        using var evilResponse = await client.SendAsync(fromEvil, Ct);

        preflightResponse.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(["*"]);
        acmeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        evilResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Each_ip_address_may_send_only_so_many_requests_a_minute()
    {
        var tenant = await SeedAsync(isPublic: true);
        await using var api = new ApiFactory(fixture);
        var client = api.CreateClient();

        for (var i = 0; i < Citra.Api.RateLimits.PublicRequestsPerIpPerMinute; i++)
        {
            using var allowed = await client.GetAsync($"/api/public/bots/{tenant.Slug}/support", Ct);
            allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var refused = await client.GetAsync($"/api/public/bots/{tenant.Slug}/support", Ct);
        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    private async Task<Tenant> SeedAsync(bool isPublic, string[]? allowedDomains = null)
    {
        var tenant = Tenant.Create("Public test company");
        tenant.ChangeSlug($"public-{Guid.NewGuid():N}");
        await using (var system = fixture.OpenScope(tenantId: null))
        {
            system.Db.Tenants.Add(tenant);
            await system.Db.SaveChangesAsync(Ct);
        }

        var bot = Bot.Create(tenant.Id, "Support");
        bot.UpdateDetails("Support", null, null, "Hi! Ask me anything.");
        bot.ConfigureWidget(isPublic, "#10b981", allowedDomains ?? [], ["Vacation?"], dailyMessageLimit: null);
        await using var scope = fixture.OpenScope(tenant.Id);
        scope.Db.Bots.Add(bot);
        await scope.Db.SaveChangesAsync(Ct);
        return tenant;
    }
}
