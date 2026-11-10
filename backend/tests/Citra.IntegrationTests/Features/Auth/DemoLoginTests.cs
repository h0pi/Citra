using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Application.Features.Auth;
using Citra.Application.Features.Auth.Demo;
using Citra.Application.Features.Auth.Register;
using Citra.Domain.Users;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Citra.IntegrationTests.Features.Auth;

/// <summary>C-256/C-257: "Try demo" logs every visitor in as an own read-only demo user, without a password.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class DemoLoginTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Settings(string? TenantSlug) : IDemoSettings;

    [Fact]
    public async Task Every_visitor_gets_an_own_read_only_demo_user()
    {
        var slug = await DemoCompanyAsync();

        var first = await DemoLoginAsync(slug);
        var second = await DemoLoginAsync(slug);

        (first.User.Role, first.User.TenantName, first.User.DisplayName).ShouldBe((UserRole.Demo, "Northwind Outdoor", "Demo visitor"));
        first.AccessToken.ShouldNotBeNullOrEmpty();
        second.User.Id.ShouldNotBe(first.User.Id, "visitors must not see each other's chats");
        second.User.TenantId.ShouldBe(first.User.TenantId);
        await using var scope = fixture.OpenScope(tenantId: null);
        (await scope.Db.Users.CountAsync(u => u.TenantId == first.User.TenantId && u.Role == UserRole.Demo, Ct)).ShouldBe(2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-such-company")]
    public async Task Without_a_demo_company_there_is_no_demo(string? slug)
    {
        await Should.ThrowAsync<NotFoundException>(() => DemoLoginAsync(slug));
    }

    [Fact]
    public async Task The_seeded_company_name_gives_the_configured_slug()
    {
        var slug = await DemoCompanyAsync();

        slug.ShouldStartWith("northwind-outdoor"); // the slug seed.py's company gets, as in appsettings.Development.json
    }

    /// <summary>Signs up "Northwind Outdoor" like demo-data/seed.py does and returns its slug.</summary>
    private async Task<string> DemoCompanyAsync()
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        var owner = await scope.Send(new RegisterCommand(
            "Northwind Outdoor", "Northwind Admin", $"owner-{Guid.NewGuid():N}@northwind-outdoor.example", "Owner-Password-123"));
        return await scope.Db.Tenants.Where(t => t.Id == owner.User.TenantId).Select(t => t.Slug).SingleAsync(Ct);
    }

    private async Task<AuthResponse> DemoLoginAsync(string? slug)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        var services = scope.Services;
        var handler = new DemoLoginCommandHandler(
            services.GetRequiredService<IApplicationDbContext>(),
            services.GetRequiredService<IIdentityService>(),
            new Settings(slug),
            services.GetRequiredService<AuthSessionIssuer>());
        return await handler.Handle(new DemoLoginCommand(), Ct);
    }
}
