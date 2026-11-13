using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Application.Features.Auth;
using Citra.Application.Features.Auth.Demo;
using Citra.Application.Features.Auth.Register;
using Citra.Domain.Conversations;
using Citra.IntegrationTests.Features.Chat;
using Citra.IntegrationTests.Infrastructure;
using Citra.Worker.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Citra.IntegrationTests.Features.Auth;

/// <summary>Every night the demo loses what visitors left — their demo users, their chats and website chats.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class DemoResetTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Settings(string? TenantSlug) : IDemoSettings;

    [Fact]
    public async Task Removes_old_demo_visitors_with_their_chats_and_old_website_chats()
    {
        var (slug, owner) = await DemoCompanyAsync();
        var bot = await ChatData.AddBotAsync(fixture, owner.User.TenantId);
        var oldVisitor = await DemoLoginAsync(slug);
        var newVisitor = await DemoLoginAsync(slug);
        var oldVisitorChat = await ChatAsync(Conversation.StartInAdmin(owner.User.TenantId, bot.Id, oldVisitor.User.Id));
        var newVisitorChat = await ChatAsync(Conversation.StartInAdmin(owner.User.TenantId, bot.Id, newVisitor.User.Id));
        var ownerChat = await ChatAsync(Conversation.StartInAdmin(owner.User.TenantId, bot.Id, owner.User.Id));
        var oldWebsiteChat = await ChatAsync(Conversation.StartInWidget(owner.User.TenantId, bot.Id, "visitor-1"));
        var newWebsiteChat = await ChatAsync(Conversation.StartInWidget(owner.User.TenantId, bot.Id, "visitor-2"));
        await using (var scope = fixture.OpenScope(tenantId: null))
        {
            await scope.Db.Users.Where(u => u.Id == oldVisitor.User.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.CreatedAt, x => x.CreatedAt.AddHours(-2)), Ct);
            await scope.Db.Conversations.IgnoreQueryFilters().Where(c => c.Id == oldWebsiteChat)
                .ExecuteUpdateAsync(c => c.SetProperty(x => x.UpdatedAt, x => x.UpdatedAt.AddHours(-2)), Ct);
        }

        var result = await ResetAsync(slug);

        result.ShouldBe(new DemoResetResult(true, 1, 2));
        await using var check = fixture.OpenScope(tenantId: null);
        (await check.Db.Users.Where(u => u.TenantId == owner.User.TenantId).Select(u => u.Id).ToListAsync(Ct))
            .ShouldBe([owner.User.Id, newVisitor.User.Id], ignoreOrder: true);
        (await check.Db.Conversations.IgnoreQueryFilters().Where(c => c.TenantId == owner.User.TenantId).Select(c => c.Id).ToListAsync(Ct))
            .ShouldBe([newVisitorChat, ownerChat, newWebsiteChat], ignoreOrder: true);
        (await check.Db.Conversations.IgnoreQueryFilters().AnyAsync(c => c.Id == oldVisitorChat, Ct)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("no-such-company")]
    public async Task Does_nothing_without_a_demo(string? slug)
    {
        (await ResetAsync(slug)).ShouldBe(new DemoResetResult(false, 0, 0));
    }

    [Theory]
    [InlineData("2026-11-10T01:00:00Z", 3, "2026-11-10T03:00:00Z")]
    [InlineData("2026-11-10T03:00:00Z", 3, "2026-11-11T03:00:00Z")]
    [InlineData("2026-11-10T22:30:00Z", 3, "2026-11-11T03:00:00Z")]
    public void Runs_at_the_next_reset_hour(string now, int hour, string next)
    {
        DemoResetService.NextRun(DateTimeOffset.Parse(now), hour).ShouldBe(DateTimeOffset.Parse(next));
    }

    private async Task<(string Slug, AuthResponse Owner)> DemoCompanyAsync()
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        var owner = await scope.Send(new RegisterCommand(
            "Northwind Outdoor", "Northwind Admin", $"owner-{Guid.NewGuid():N}@northwind-outdoor.example", "Owner-Password-123"));
        var slug = await scope.Db.Tenants.Where(t => t.Id == owner.User.TenantId).Select(t => t.Slug).SingleAsync(Ct);
        return (slug, owner);
    }

    private async Task<AuthResponse> DemoLoginAsync(string slug)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        var services = scope.Services;
        return await new DemoLoginCommandHandler(
            services.GetRequiredService<IApplicationDbContext>(),
            services.GetRequiredService<IIdentityService>(),
            new Settings(slug),
            services.GetRequiredService<AuthSessionIssuer>()).Handle(new DemoLoginCommand(), Ct);
    }

    private async Task<Guid> ChatAsync(Conversation conversation)
    {
        conversation.AddUserMessage("Question?");
        await using var scope = fixture.OpenScope(conversation.TenantId);
        scope.Db.Conversations.Add(conversation);
        await scope.Db.SaveChangesAsync(Ct);
        return conversation.Id;
    }

    private async Task<DemoResetResult> ResetAsync(string? slug)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        var services = scope.Services;
        return await new DemoReset(
            services.GetRequiredService<IApplicationDbContext>(),
            services.GetRequiredService<IIdentityService>(),
            new Settings(slug),
            TimeProvider.System).RunAsync(Ct);
    }
}
