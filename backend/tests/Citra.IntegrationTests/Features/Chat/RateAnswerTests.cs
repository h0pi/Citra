using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Chat.Feedback;
using Citra.Domain.Conversations;
using Citra.Domain.Users;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Citra.IntegrationTests.Features.Chat.ChatData;

namespace Citra.IntegrationTests.Features.Chat;

/// <summary>C-187: thumbs up / down on answers, and the chat endpoints over HTTP (C-185).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class RateAnswerTests(DatabaseFixture fixture)
{
    private const string VacationChunk = "Employees get 25 vacation days per year.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rates_an_answer_up_down_and_back_to_none()
    {
        var seed = await SeedAsync(fixture, [VacationChunk]);
        var (_, answerId) = await QuestionAndAnswerAsync(seed);

        foreach (var feedback in new[] { MessageFeedback.Positive, MessageFeedback.Negative, MessageFeedback.None })
        {
            await using (var scope = Scope(fixture, seed))
            {
                await scope.Send(new RateAnswerCommand(answerId, feedback));
            }

            await using var check = fixture.OpenScope(seed.Tenant.Id);
            (await check.Db.Messages.SingleAsync(m => m.Id == answerId, Ct)).Feedback.ShouldBe(feedback);
        }
    }

    [Fact]
    public async Task A_question_cannot_be_rated()
    {
        var seed = await SeedAsync(fixture, [VacationChunk]);
        var (questionId, _) = await QuestionAndAnswerAsync(seed);

        await using var scope = Scope(fixture, seed);

        var error = await Should.ThrowAsync<ValidationException>(() => scope.Send(new RateAnswerCommand(questionId, MessageFeedback.Positive)));
        error.Errors["MessageId"].ShouldBe(["Only answers can be rated, not questions."]);
    }

    [Fact]
    public async Task Only_answers_in_the_users_own_chats_can_be_rated()
    {
        var seed = await SeedAsync(fixture, [VacationChunk]);
        var (_, answerId) = await QuestionAndAnswerAsync(seed);
        var colleague = seed with { UserId = await CreateUserAsync(fixture, seed.Tenant.Id, UserRole.Owner), Role = UserRole.Owner };
        var outsider = await SeedAsync(fixture, []);

        await using (var scope = Scope(fixture, colleague))
        {
            await Should.ThrowAsync<NotFoundException>(() => scope.Send(new RateAnswerCommand(answerId, MessageFeedback.Negative)));
        }

        await using (var scope = Scope(fixture, outsider))
        {
            await Should.ThrowAsync<NotFoundException>(() => scope.Send(new RateAnswerCommand(answerId, MessageFeedback.Negative)));
            await Should.ThrowAsync<NotFoundException>(() => scope.Send(new RateAnswerCommand(Guid.NewGuid(), MessageFeedback.Negative)));
        }

        await using var check = fixture.OpenScope(seed.Tenant.Id);
        (await check.Db.Messages.SingleAsync(m => m.Id == answerId, Ct)).Feedback.ShouldBe(MessageFeedback.None);
    }

    [Theory]
    [InlineData(true, 7)]
    [InlineData(false, 1)]
    public async Task Refuses_an_unknown_rating_or_message_id(bool realMessage, int feedback)
    {
        var seed = await SeedAsync(fixture, [VacationChunk]);
        var (_, answerId) = await QuestionAndAnswerAsync(seed);

        await using var scope = Scope(fixture, seed);

        await Should.ThrowAsync<ValidationException>(
            () => scope.Send(new RateAnswerCommand(realMessage ? answerId : Guid.Empty, (MessageFeedback)feedback)));
    }

    [Fact]
    public async Task The_demo_account_may_rate_its_own_answers()
    {
        var seed = await SeedAsync(fixture, [VacationChunk], UserRole.Demo);
        var (_, answerId) = await QuestionAndAnswerAsync(seed);

        await using var scope = Scope(fixture, seed);
        await scope.Send(new RateAnswerCommand(answerId, MessageFeedback.Positive));
    }

    [Fact]
    public async Task Nobody_logged_in_is_refused()
    {
        await using var scope = fixture.OpenScope(tenantId: null);

        await Should.ThrowAsync<UnauthorizedException>(() => scope.Send(new RateAnswerCommand(Guid.NewGuid(), MessageFeedback.Positive)));
    }

    // ---------- over HTTP ----------

    [Fact]
    public async Task The_chat_endpoints_list_open_rate_and_delete_chats()
    {
        await using var api = new ApiFactory(fixture);
        var user = await api.SignUpAsync(Ct);
        var bot = await AddBotAsync(fixture, user.TenantId, [VacationChunk]);
        using var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        using (var ask = await client.PostAsJsonAsync($"/api/bots/{bot.Id}/chat", new { question = "How many vacation days?" }, Ct))
        {
            await ask.Content.ReadAsStringAsync(Ct);
        }

        var list = await client.GetFromJsonAsync<JsonElement>($"/api/bots/{bot.Id}/conversations?page=1&pageSize=10", Ct);
        list.GetProperty("totalCount").GetInt32().ShouldBe(1);
        var summary = list.GetProperty("items")[0];
        summary.GetProperty("title").GetString().ShouldBe("How many vacation days?");
        summary.GetProperty("messageCount").GetInt32().ShouldBe(2);
        var id = summary.GetProperty("id").GetGuid();

        var chat = await client.GetFromJsonAsync<JsonElement>($"/api/conversations/{id}", Ct);
        var messages = chat.GetProperty("messages").EnumerateArray().ToList();
        messages.Select(m => m.GetProperty("role").GetString()).ShouldBe(["User", "Assistant"]);
        messages[1].GetProperty("feedback").GetString().ShouldBe("None");
        messages[1].GetProperty("citations")[0].GetProperty("documentName").GetString().ShouldBe("Handbook.pdf");
        var answerId = messages[1].GetProperty("id").GetGuid();

        (await client.PutAsJsonAsync($"/api/messages/{answerId}/feedback", new { feedback = "Positive" }, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<JsonElement>($"/api/conversations/{id}", Ct)).GetProperty("messages")[1]
            .GetProperty("feedback").GetString().ShouldBe("Positive");
        (await client.PutAsJsonAsync($"/api/messages/{answerId}/feedback", new { feedback = "Excellent" }, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync($"/api/messages/{messages[0].GetProperty("id").GetGuid()}/feedback", new { feedback = "Positive" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await client.DeleteAsync($"/api/conversations/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/conversations/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/bots/{Guid.NewGuid()}/conversations", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/bots/{bot.Id}/conversations?pageSize=500", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        client.DefaultRequestHeaders.Authorization = null;
        (await client.GetAsync($"/api/bots/{bot.Id}/conversations", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>One question asked through the real chat; the ids of the question and its answer.</summary>
    private async Task<(Guid QuestionId, Guid AnswerId)> QuestionAndAnswerAsync(ChatSeed seed)
    {
        var conversationId = await AskAsync(fixture, seed, "How many vacation days?");
        await using var check = fixture.OpenScope(seed.Tenant.Id);
        var messages = await check.Db.Messages.Where(m => m.ConversationId == conversationId).ToListAsync(Ct);
        return (messages.Single(m => m.Role == MessageRole.User).Id, messages.Single(m => m.Role == MessageRole.Assistant).Id);
    }
}
