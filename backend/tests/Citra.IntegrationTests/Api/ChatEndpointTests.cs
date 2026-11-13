using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Citra.Api.Chat;
using Citra.Application.Common.Chat;
using Citra.Application.Common.Embeddings;
using Citra.Domain.Bots;
using Citra.Domain.Conversations;
using Citra.Domain.Documents;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Citra.IntegrationTests.Api;

/// <summary>
/// POST /api/bots/{botId}/chat streams the answer as server-sent events (start, citations,
/// token…, done; or error), and stops the model when the browser disconnects.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ChatEndpointTests(DatabaseFixture fixture)
{
    private const string Question = "How many vacation days do I get?";
    private const string VacationChunk = "Employees get 25 vacation days per year.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ChatUsage Usage = new("claude-test", 100, 10);

    [Fact]
    public async Task Streams_start_citations_tokens_and_done_as_server_sent_events()
    {
        await using var api = new ApiFactory(fixture);
        var (client, bot) = await SignedInWithBotAsync(api, VacationChunk);

        using var response = await client.PostAsJsonAsync($"/api/bots/{bot.Id}/chat", new { question = Question }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");
        var events = Parse(await response.Content.ReadAsStringAsync(Ct));

        events.Select(e => e.Type).Distinct().ShouldBe([ChatEvents.Start, ChatEvents.Citations, ChatEvents.Token, ChatEvents.Done]);
        events[0].Type.ShouldBe(ChatEvents.Start);
        events[1].Type.ShouldBe(ChatEvents.Citations);
        events[^1].Type.ShouldBe(ChatEvents.Done);
        events.Skip(2).SkipLast(1).ShouldAllBe(e => e.Type == ChatEvents.Token);

        var start = events[0].Data;
        start.GetProperty("searchQuery").GetString().ShouldBe(Question);
        var conversationId = start.GetProperty("conversationId").GetGuid();
        start.GetProperty("questionId").GetGuid().ShouldNotBe(Guid.Empty);

        var source = events[1].Data.GetProperty("sources").EnumerateArray().ShouldHaveSingleItem();
        source.GetProperty("number").GetInt32().ShouldBe(1);
        source.GetProperty("documentName").GetString().ShouldBe("Handbook.pdf");
        source.GetProperty("pageNumber").GetInt32().ShouldBe(1);
        source.GetProperty("heading").GetString().ShouldBe("Leave");
        source.GetProperty("content").GetString().ShouldBe(VacationChunk);
        source.TryGetProperty("embedding", out _).ShouldBeFalse("vectors are never sent to the browser");

        var text = string.Concat(events.Where(e => e.Type == ChatEvents.Token).Select(e => e.Data.GetProperty("text").GetString()));
        var done = events[^1].Data;
        done.GetProperty("content").GetString().ShouldBe(text);
        done.GetProperty("wasAnswered").GetBoolean().ShouldBeTrue();
        done.GetProperty("citedSources").EnumerateArray().Select(n => n.GetInt32()).ShouldBe([1]);
        text.ShouldBe("Employees get 25 vacation days per year [1].");

        await using var check = fixture.OpenScope(bot.TenantId);
        var saved = await check.Db.Messages.SingleAsync(m => m.Id == done.GetProperty("messageId").GetGuid(), Ct);
        (saved.ConversationId, saved.Role, saved.Content).ShouldBe((conversationId, MessageRole.Assistant, text));
    }

    [Fact]
    public async Task Continues_a_conversation_given_its_id()
    {
        await using var api = new ApiFactory(fixture);
        var (client, bot) = await SignedInWithBotAsync(api, VacationChunk);
        using var first = await client.PostAsJsonAsync($"/api/bots/{bot.Id}/chat", new { question = Question }, Ct);
        var conversationId = Parse(await first.Content.ReadAsStringAsync(Ct))[0].Data.GetProperty("conversationId").GetGuid();

        using var second = await client.PostAsJsonAsync($"/api/bots/{bot.Id}/chat", new { conversationId, question = "And next year?" }, Ct);

        Parse(await second.Content.ReadAsStringAsync(Ct))[0].Data.GetProperty("conversationId").GetGuid().ShouldBe(conversationId);
        await using var check = fixture.OpenScope(bot.TenantId);
        (await check.Db.Messages.CountAsync(m => m.ConversationId == conversationId, Ct)).ShouldBe(4);
    }

    // ---------- failures before streaming: ordinary HTTP errors ----------

    [Fact]
    public async Task Problems_found_before_streaming_are_ordinary_http_errors()
    {
        await using var api = new ApiFactory(fixture);
        var (client, bot) = await SignedInWithBotAsync(api, VacationChunk);

        var empty = await client.PostAsJsonAsync($"/api/bots/{bot.Id}/chat", new { question = "  " }, Ct);
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await empty.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").TryGetProperty("question", out _).ShouldBeTrue();

        (await client.PostAsJsonAsync($"/api/bots/{bot.Id}/chat", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync($"/api/bots/{Guid.NewGuid()}/chat", new { question = Question }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync($"/api/bots/{bot.Id}/chat", new { conversationId = Guid.NewGuid(), question = Question }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        client.DefaultRequestHeaders.Authorization = null;
        (await client.PostAsJsonAsync($"/api/bots/{bot.Id}/chat", new { question = Question }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_failing_search_is_a_503_before_anything_streams()
    {
        await using var api = new ApiFactory(fixture);
        var (client, bot) = await SignedInWithBotAsync(api, VacationChunk);

        HttpResponseMessage response;
        using (fixture.Embeddings.Intercept((_, _) => throw new EmbeddingException("Voyage AI rate limit reached (429).")))
        {
            response = await client.PostAsJsonAsync($"/api/bots/{bot.Id}/chat", new { question = Question }, Ct);
        }

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    // ---------- failures while streaming: a final error event ----------

    [Fact]
    public async Task A_model_failure_while_answering_ends_with_a_friendly_error_event()
    {
        await using var api = new ApiFactory(fixture);
        var (client, bot) = await SignedInWithBotAsync(api, VacationChunk);

        List<(string Type, JsonElement Data)> events;
        using (fixture.Chat.Script(_ => FakeChatModel.Failing(new ChatModelException("Claude is overloaded (529): Overloaded"), "Employees ")))
        {
            using var response = await client.PostAsJsonAsync($"/api/bots/{bot.Id}/chat", new { question = Question }, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            events = Parse(await response.Content.ReadAsStringAsync(Ct));
        }

        events.Select(e => e.Type).ShouldBe([ChatEvents.Start, ChatEvents.Citations, ChatEvents.Token, ChatEvents.Error]);
        events[^1].Data.GetProperty("message").GetString().ShouldBe(ChatEvents.UnavailableMessage);
        events[^1].Data.GetRawText().ShouldNotContain("529", Case.Sensitive, "internal details stay in the logs");
    }

    [Fact]
    public async Task An_unexpected_failure_while_answering_ends_with_a_general_error_event()
    {
        await using var api = new ApiFactory(fixture);
        var (client, bot) = await SignedInWithBotAsync(api, VacationChunk);

        List<(string Type, JsonElement Data)> events;
        using (fixture.Chat.Script(_ => FakeChatModel.Failing(new InvalidOperationException("Secret internal detail"))))
        {
            using var response = await client.PostAsJsonAsync($"/api/bots/{bot.Id}/chat", new { question = Question }, Ct);
            events = Parse(await response.Content.ReadAsStringAsync(Ct));
        }

        events.Select(e => e.Type).ShouldBe([ChatEvents.Start, ChatEvents.Citations, ChatEvents.Error]);
        events[^1].Data.GetProperty("message").GetString().ShouldBe(ChatEvents.FailedMessage);
        events[^1].Data.GetRawText().ShouldNotContain("Secret");
    }

    // ---------- Leaving stops the model ----------

    [Fact]
    public async Task Closing_the_connection_stops_the_model_and_saves_no_answer()
    {
        await using var api = new ApiFactory(fixture);
        var (client, bot) = await SignedInWithBotAsync(api, VacationChunk);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var written = 0;

        async IAsyncEnumerable<ChatEvent> EndlessAnswer([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            try
            {
                while (true)
                {
                    written++;
                    yield return new ChatTextDelta("word ");
                    await Task.Delay(20, cancellationToken);
                }
            }
            finally
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    stopped.TrySetResult();
                }
            }
        }

        using (fixture.Chat.Script(_ => EndlessAnswer()))
        {
            using var leave = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/bots/{bot.Id}/chat") { Content = JsonContent.Create(new { question = Question }) };
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, leave.Token);
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(leave.Token));
            while (await reader.ReadLineAsync(leave.Token) is { } line && line != $"event: {ChatEvents.Token}")
            {
            }

            await leave.CancelAsync(); // the user closes the page
            response.Dispose();

            (await Task.WhenAny(stopped.Task, Task.Delay(TimeSpan.FromSeconds(10), Ct))).ShouldBe(stopped.Task, "the model was cancelled");
        }

        var wordsWhenStopped = written;
        await Task.Delay(200, Ct);
        written.ShouldBe(wordsWhenStopped, "nothing more is generated");
        await using var check = fixture.OpenScope(bot.TenantId);
        var messages = await check.Db.Conversations.Where(c => c.BotId == bot.Id).SelectMany(c => c.Messages).ToListAsync(Ct);
        messages.ShouldHaveSingleItem().Role.ShouldBe(MessageRole.User);
    }

    [Fact]
    public void Every_event_kind_has_its_own_name()
    {
        new[] { ChatEvents.Start, ChatEvents.Citations, ChatEvents.Token, ChatEvents.Done, ChatEvents.Error }
            .ShouldBe(["start", "citations", "token", "done", "error"]);
    }

    // ---------- helpers ----------

    /// <summary>A signed-up owner's client and a bot of their company with one Ready document of the given chunks.</summary>
    private async Task<(HttpClient Client, Bot Bot)> SignedInWithBotAsync(ApiFactory api, params string[] chunks)
    {
        var user = await api.SignUpAsync(Ct);
        var bot = Bot.Create(user.TenantId, "Support");
        bot.ChangeSlug($"support-{Guid.NewGuid():N}");
        var document = Document.FromUpload(
            user.TenantId, bot.Id, "Handbook.pdf", "application/pdf", 100, DocumentSourceType.Pdf,
            $"{user.TenantId}/{bot.Id}/{Guid.NewGuid():N}/original.pdf", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
        var start = 0;
        var entities = chunks.Select((text, i) =>
        {
            var chunk = Chunk.Create(document.Id, bot.Id, i, text, "Leave", 1, start, start + text.Length, 10);
            start += text.Length + 1;
            chunk.SetEmbedding(FakeEmbeddingService.VectorFor(text));
            return chunk;
        }).ToList();

        await using (var scope = fixture.OpenScope(tenantId: null))
        {
            scope.Db.AddRange(bot, document);
            scope.Db.Chunks.AddRange(entities);
            await scope.Db.SaveChangesAsync(Ct);
        }

        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        return (client, bot);
    }

    /// <summary>"event: x\ndata: {json}\n\n" blocks → (x, json).</summary>
    private static List<(string Type, JsonElement Data)> Parse(string body)
    {
        var events = new List<(string, JsonElement)>();
        foreach (var block in body.ReplaceLineEndings("\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = block.Split('\n');
            var type = lines.Single(l => l.StartsWith("event: ", StringComparison.Ordinal))["event: ".Length..];
            var data = new StringBuilder();
            foreach (var line in lines.Where(l => l.StartsWith("data: ", StringComparison.Ordinal)))
            {
                data.Append(line["data: ".Length..]);
            }

            events.Add((type, JsonDocument.Parse(data.ToString()).RootElement.Clone()));
        }

        return events;
    }
}
