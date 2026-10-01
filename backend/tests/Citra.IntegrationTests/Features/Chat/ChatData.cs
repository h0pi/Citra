using Citra.Application.Features.Chat.SendMessage;
using Citra.Domain.Bots;
using Citra.Domain.Conversations;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.Domain.Users;
using Citra.Infrastructure.Identity;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Citra.IntegrationTests.Features.Chat;

/// <summary>A company with a user and a bot, for chat tests.</summary>
internal sealed record ChatSeed(Tenant Tenant, Guid UserId, UserRole Role, Bot Bot);

/// <summary>Creates companies, users, bots with chunks and conversations, and scopes acting as a user.</summary>
internal static class ChatData
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A company with a user and a bot "Support" whose PDF has the given chunks (page 1, section "Leave").</summary>
    public static async Task<ChatSeed> SeedAsync(DatabaseFixture fixture, IReadOnlyList<string> chunks, UserRole role = UserRole.Admin)
    {
        var tenant = Tenant.Create("Chat company");
        tenant.ChangeSlug($"chat-{Guid.NewGuid():N}");
        await using (var scope = fixture.OpenScope(tenantId: null))
        {
            scope.Db.Tenants.Add(tenant);
            await scope.Db.SaveChangesAsync(Ct);
        }

        var userId = await CreateUserAsync(fixture, tenant.Id, role);
        return new ChatSeed(tenant, userId, role, await AddBotAsync(fixture, tenant.Id, chunks));
    }

    public static async Task<Bot> AddBotAsync(DatabaseFixture fixture, Guid tenantId, IReadOnlyList<string>? chunks = null)
    {
        var bot = Bot.Create(tenantId, "Support");
        bot.ChangeSlug($"support-{Guid.NewGuid():N}");
        var document = Document.FromUpload(
            tenantId, bot.Id, "Handbook.pdf", "application/pdf", 100, DocumentSourceType.Pdf,
            $"{tenantId}/{bot.Id}/{Guid.NewGuid():N}/original.pdf", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
        var start = 0;
        var entities = (chunks ?? []).Select((text, i) =>
        {
            var chunk = Chunk.Create(document.Id, bot.Id, i, text, "Leave", 1, start, start + text.Length, 10);
            start += text.Length + 1;
            chunk.SetEmbedding(FakeEmbeddingService.VectorFor(text));
            return chunk;
        }).ToList();

        await using var scope = fixture.OpenScope(tenantId: null);
        scope.Db.AddRange(bot, document);
        scope.Db.Chunks.AddRange(entities);
        await scope.Db.SaveChangesAsync(Ct);
        return bot;
    }

    public static async Task<Guid> CreateUserAsync(DatabaseFixture fixture, Guid tenantId, UserRole role)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        var email = $"user-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { TenantId = tenantId, UserName = email, Email = email, DisplayName = "Tester", Role = role };
        (await scope.Services.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user)).Succeeded.ShouldBeTrue();
        return user.Id;
    }

    /// <summary>A unit of work acting as the seed's user.</summary>
    public static DbScope Scope(DatabaseFixture fixture, ChatSeed seed)
    {
        var scope = fixture.OpenScope(seed.Tenant.Id, seed.Role);
        scope.Services.GetRequiredService<TestCurrentUser>().UserId = seed.UserId;
        return scope;
    }

    /// <summary>Asks a question through the real chat (stand-in model) and returns the conversation id.</summary>
    public static async Task<Guid> AskAsync(DatabaseFixture fixture, ChatSeed seed, string question, Guid? conversationId = null)
    {
        await using var scope = Scope(fixture, seed);
        var events = await scope.Stream(new SendMessageCommand(seed.Bot.Id, conversationId, question));
        return ((MessageStarted)events[0]).ConversationId;
    }

    /// <summary>
    /// A conversation of the seed's user whose question and answer were saved together (same timestamp), as
    /// imported or seeded data can be: only the role can order them.
    /// </summary>
    public static async Task<Guid> SavedTogetherAsync(DatabaseFixture fixture, ChatSeed seed)
    {
        await using var scope = fixture.OpenScope(seed.Tenant.Id);
        var conversation = Conversation.StartInAdmin(seed.Tenant.Id, seed.Bot.Id, seed.UserId);
        // Added answer first: the database gets no help from the insert order.
        var answer = conversation.AddAssistantMessage(string.Empty);
        answer.CompleteAnswer("Saved answer.", wasAnswered: true);
        conversation.AddUserMessage("Saved question?");
        scope.Db.Conversations.Add(conversation);
        await scope.Db.SaveChangesAsync(Ct);
        return conversation.Id;
    }

    /// <summary>A website-widget conversation with the bot (not any user's).</summary>
    public static async Task<Guid> WidgetConversationAsync(DatabaseFixture fixture, ChatSeed seed)
    {
        await using var scope = fixture.OpenScope(seed.Tenant.Id);
        var conversation = Conversation.StartInWidget(seed.Tenant.Id, seed.Bot.Id, "visitor-1");
        conversation.AddUserMessage("Widget question?");
        scope.Db.Conversations.Add(conversation);
        await scope.Db.SaveChangesAsync(Ct);
        return conversation.Id;
    }
}
