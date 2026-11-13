using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Citra.IntegrationTests.Api;

/// <summary>
/// One company can never reach another's data through any main endpoint. Company A gets a bot, a document,
/// a chat and an evaluation; company B (an owner, so roles are no excuse) tries every endpoint that takes their
/// ids: each answers 404, as for an id that does not exist, and A's data is unchanged afterwards.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed partial class TenantIsolationEndpointTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Another_companys_ids_are_not_found_on_any_endpoint_and_change_nothing()
    {
        await using var api = new ApiFactory(fixture);
        var a = await SeedCompanyAAsync(api);
        using var stranger = await ClientAsync(api);
        var updateBot = new
        {
            name = "Taken over", description = (string?)null, systemPrompt = (string?)null, welcomeMessage = "Hi",
            model = (string?)null, widgetColor = "#6366F1", starterQuestions = Array.Empty<string>(),
            isPublic = true, allowedDomains = Array.Empty<string>(), dailyMessageLimit = (int?)null,
        };

        (HttpMethod Method, string Url, object? Body)[] attempts =
        [
            // Bots
            (HttpMethod.Get, $"/api/bots/{a.BotId}", null),
            (HttpMethod.Put, $"/api/bots/{a.BotId}", updateBot),
            (HttpMethod.Delete, $"/api/bots/{a.BotId}", null),
            (HttpMethod.Get, $"/api/bots/{a.BotId}/search?q=vacation", null),
            // Documents
            (HttpMethod.Get, $"/api/bots/{a.BotId}/documents", null),
            (HttpMethod.Post, $"/api/bots/{a.BotId}/documents/url", new { url = "https://example.com/page" }),
            (HttpMethod.Get, $"/api/documents/{a.DocumentId}", null),
            (HttpMethod.Get, $"/api/documents/{a.DocumentId}/chunks", null),
            (HttpMethod.Get, $"/api/documents/{a.DocumentId}/file", null),
            (HttpMethod.Post, $"/api/documents/{a.DocumentId}/reprocess", null),
            (HttpMethod.Delete, $"/api/documents/{a.DocumentId}", null),
            // Chat
            (HttpMethod.Post, $"/api/bots/{a.BotId}/chat", new { question = "How many vacation days do I get?" }),
            (HttpMethod.Get, $"/api/bots/{a.BotId}/conversations", null),
            (HttpMethod.Get, $"/api/conversations/{a.ConversationId}", null),
            (HttpMethod.Put, $"/api/conversations/{a.ConversationId}/title", new { title = "Taken over" }),
            (HttpMethod.Delete, $"/api/conversations/{a.ConversationId}", null),
            (HttpMethod.Put, $"/api/messages/{a.AnswerId}/feedback", new { feedback = "Negative" }),
            (HttpMethod.Get, $"/api/analytics/conversations/{a.ConversationId}", null),
            // Evaluation
            (HttpMethod.Get, $"/api/bots/{a.BotId}/eval-sets", null),
            (HttpMethod.Post, $"/api/bots/{a.BotId}/eval-sets", new { name = "Taken over" }),
            (HttpMethod.Get, $"/api/eval-sets/{a.EvalSetId}", null),
            (HttpMethod.Put, $"/api/eval-sets/{a.EvalSetId}", new { name = "Taken over" }),
            (HttpMethod.Delete, $"/api/eval-sets/{a.EvalSetId}", null),
            (HttpMethod.Post, $"/api/eval-sets/{a.EvalSetId}/questions", new { question = "Q?", expectedAnswer = "A." }),
            (HttpMethod.Put, $"/api/eval-sets/{a.EvalSetId}/questions/{a.QuestionId}", new { question = "Q?", expectedAnswer = "A." }),
            (HttpMethod.Delete, $"/api/eval-sets/{a.EvalSetId}/questions/{a.QuestionId}", null),
            (HttpMethod.Post, $"/api/eval-sets/{a.EvalSetId}/questions/import", new { csv = "question,expected_answer\nQ?,A.\n" }),
            (HttpMethod.Post, $"/api/eval-sets/{a.EvalSetId}/runs", new { }),
            (HttpMethod.Get, $"/api/eval-sets/{a.EvalSetId}/runs", null),
            (HttpMethod.Get, $"/api/eval-runs/{a.RunId}", null),
            (HttpMethod.Get, $"/api/eval-runs/{a.RunId}/results", null),
        ];

        var answers = new List<string>();
        foreach (var (method, url, body) in attempts)
        {
            using var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
            using var response = await stranger.SendAsync(request, Ct);
            answers.Add($"{method} {url} → {(int)response.StatusCode}");
        }

        answers.ShouldAllBe(answer => answer.EndsWith("→ 404"), string.Join('\n', answers.Where(x => !x.EndsWith("→ 404"))));
        // The same addresses work for A (a mistyped address would be a 404 for everyone and prove nothing).
        foreach (var (_, url, _) in attempts.Where(x => x.Method == HttpMethod.Get && !x.Url.EndsWith("/file")))
        {
            (await a.Client.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK, url);
        }

        // Uploading needs a multipart body.
        using (var upload = new MultipartFormDataContent { { new ByteArrayContent("# Taken over"u8.ToArray()), "file", "x.md" } })
        {
            (await stranger.PostAsync($"/api/bots/{a.BotId}/documents", upload, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        // Nothing of A's was changed or removed.
        (await JsonAsync(a.Client, $"/api/bots/{a.BotId}")).GetProperty("name").GetString().ShouldBe("Support");
        (await JsonAsync(a.Client, $"/api/documents/{a.DocumentId}")).GetProperty("fileName").GetString().ShouldBe("Handbook.pdf");
        var conversation = await JsonAsync(a.Client, $"/api/conversations/{a.ConversationId}");
        conversation.GetProperty("title").GetString().ShouldNotBe("Taken over");
        conversation.GetProperty("messages")[1].GetProperty("feedback").GetString().ShouldBe("None");
        var set = await JsonAsync(a.Client, $"/api/eval-sets/{a.EvalSetId}");
        (set.GetProperty("name").GetString(), set.GetProperty("questions").GetArrayLength()).ShouldBe(("HR", 1));
        (await JsonAsync(a.Client, $"/api/eval-sets/{a.EvalSetId}/runs")).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Another_companys_bot_shows_no_numbers_on_the_dashboard()
    {
        await using var api = new ApiFactory(fixture);
        var a = await SeedCompanyAAsync(api);
        using var stranger = await ClientAsync(api);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var period = $"botId={a.BotId}&from={today.AddDays(-7):yyyy-MM-dd}&to={today:yyyy-MM-dd}";

        var own = await JsonAsync(a.Client, $"/api/analytics/stats?{period}");
        var theirs = await JsonAsync(stranger, $"/api/analytics/stats?{period}");
        var theirChats = await JsonAsync(stranger, $"/api/analytics/conversations?botId={a.BotId}");

        own.GetProperty("conversations").GetInt32().ShouldBe(1);
        (theirs.GetProperty("conversations").GetInt32(), theirs.GetProperty("questions").GetInt32()).ShouldBe((0, 0));
        theirChats.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    private sealed record CompanyA(
        HttpClient Client, Guid BotId, Guid DocumentId, Guid ConversationId, Guid AnswerId, Guid EvalSetId, Guid QuestionId, Guid RunId);

    /// <summary>Company A: a bot with a ready document, one chat (question + answer) and an eval set with a run.</summary>
    private async Task<CompanyA> SeedCompanyAAsync(ApiFactory api)
    {
        var client = await ClientAsync(api);
        var tenantId = TenantIdOf(client);
        var bot = Bot.Create(tenantId, "Support");
        bot.ChangeSlug($"support-{Guid.NewGuid():N}");
        var document = Document.FromUpload(
            tenantId, bot.Id, "Handbook.pdf", "application/pdf", 100, DocumentSourceType.Pdf,
            $"{tenantId}/{bot.Id}/{Guid.NewGuid():N}/original.pdf", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
        const string text = "Employees get 25 vacation days per year.";
        var chunk = Chunk.Create(document.Id, bot.Id, 0, text, "Leave", 1, 0, text.Length, 10);
        chunk.SetEmbedding(FakeEmbeddingService.VectorFor(text));
        await using (var scope = fixture.OpenScope(tenantId: null))
        {
            scope.Db.AddRange(bot, document, chunk);
            await scope.Db.SaveChangesAsync(Ct);
        }

        using var chat = await client.PostAsJsonAsync($"/api/bots/{bot.Id}/chat", new { question = "How many vacation days do I get?" }, Ct);
        var events = await chat.Content.ReadAsStringAsync(Ct);
        var conversationId = Guid.Parse(ConversationIdIn().Match(events).Groups[1].Value);
        var answerId = Guid.Parse(MessageIdIn().Match(events).Groups[1].Value);

        var set = await PostAsync(client, $"/api/bots/{bot.Id}/eval-sets", new { name = "HR" });
        var question = await PostAsync(client, $"/api/eval-sets/{set}/questions", new { question = "Vacation?", expectedAnswer = "25 days." });
        var run = await PostAsync(client, $"/api/eval-sets/{set}/runs", new { });

        // There is no Worker in tests: end the run here, or the next test that runs pending evals would run it.
        await using (var scope = fixture.OpenScope(tenantId: null))
        {
            var pending = await scope.Db.EvalRuns.IgnoreQueryFilters().SingleAsync(r => r.Id == run, Ct);
            pending.Start(DateTimeOffset.UtcNow);
            pending.Fail("Not run in this test.", DateTimeOffset.UtcNow);
            await scope.Db.SaveChangesAsync(Ct);
        }

        return new CompanyA(client, bot.Id, document.Id, conversationId, answerId, set, question, run);
    }

    private static async Task<HttpClient> ClientAsync(ApiFactory api)
    {
        var user = await api.SignUpAsync(Ct);
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        return client;
    }

    private static Guid TenantIdOf(HttpClient client)
    {
        var payload = client.DefaultRequestHeaders.Authorization!.Parameter!.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        using var claims = JsonDocument.Parse(Convert.FromBase64String(payload));
        return Guid.Parse(claims.RootElement.GetProperty("tenant_id").GetString()!);
    }

    private static async Task<Guid> PostAsync(HttpClient client, string url, object body)
    {
        using var response = await client.PostAsJsonAsync(url, body, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> JsonAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, url);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    [GeneratedRegex("\"conversationId\":\"([0-9a-f-]{36})\"")]
    private static partial Regex ConversationIdIn();

    [GeneratedRegex("\"messageId\":\"([0-9a-f-]{36})\"")]
    private static partial Regex MessageIdIn();
}
