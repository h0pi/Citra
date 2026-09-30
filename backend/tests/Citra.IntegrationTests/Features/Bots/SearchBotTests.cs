using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Citra.Application.Common.Embeddings;
using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Bots.Search;
using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.Domain.Usage;
using Citra.Domain.Users;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Citra.IntegrationTests.Features.Bots;

/// <summary>C-169: the search playground's endpoint — what the bot finds for a question, with every score.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class SearchBotTests(DatabaseFixture fixture)
{
    private const string Question = "How many vacation days do I get?";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Shows_what_the_bot_finds_best_first_with_every_score()
    {
        var (tenant, bot, chunks) = await SeedAsync(
            ("Parking is free for employees.", FakeEmbeddingService.VectorFor("parking")),
            ("Employees get 25 vacation days.", FakeEmbeddingService.VectorFor(Question)));

        await using var scope = fixture.OpenScope(tenant.Id, UserRole.Admin);
        var result = await scope.Send(new SearchBotQuery(bot.Id, $"  {Question} "));

        result.Query.ShouldBe(Question);
        result.QueryTokens.ShouldBe(FakeEmbeddingService.TokensFor(Question));
        (result.MinSimilarity, result.MinKeywordSimilarity).ShouldBe((-1, -1)); // the tests' settings
        result.Results.Select(r => r.Rank).ShouldBe([1, 2]);
        var best = result.Results[0];
        best.ShouldBe(new SearchResultDto(
            1, chunks[1].Id, chunks[1].DocumentId, "Handbook.pdf", 1, 3, "Benefits", "Employees get 25 vacation days.",
            chunks[1].CharStart, chunks[1].CharEnd, best.Score, best.Similarity, best.KeywordScore, 1, 1, 1));
        best.Similarity!.Value.ShouldBe(1, tolerance: 1e-6);
        best.KeywordScore!.Value.ShouldBeGreaterThan(0);
        best.Score.ShouldBeGreaterThan(result.Results[1].Score);
    }

    [Fact]
    public async Task Returns_at_most_top_k()
    {
        var (tenant, bot, _) = await SeedAsync(
            [.. Enumerable.Range(1, 5).Select(i => ($"Chunk {i}.", FakeEmbeddingService.VectorFor($"chunk {i}")))]);

        await using var scope = fixture.OpenScope(tenant.Id);

        (await scope.Send(new SearchBotQuery(bot.Id, Question, TopK: 3))).Results.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Records_what_the_question_cost()
    {
        var (tenant, bot, _) = await SeedAsync(("Anything.", FakeEmbeddingService.VectorFor("anything")));

        await using (var scope = fixture.OpenScope(tenant.Id))
        {
            await scope.Send(new SearchBotQuery(bot.Id, Question));
        }

        await using var check = fixture.OpenScope(tenant.Id);
        var usage = await check.Db.EmbeddingUsages.SingleAsync(u => u.BotId == bot.Id, Ct);
        usage.Purpose.ShouldBe(EmbeddingPurpose.Query);
        usage.TenantId.ShouldBe(tenant.Id);
        usage.DocumentId.ShouldBeNull();
        usage.Model.ShouldBe(FakeEmbeddingService.ModelName);
        usage.Tokens.ShouldBe(FakeEmbeddingService.TokensFor(Question));
        usage.CostUsd.ShouldBe(EmbeddingCost.Calculate(FakeEmbeddingService.TokensFor(Question), fixture.Embeddings.PricePerMillionTokensUsd));
    }

    [Fact]
    public async Task Owners_may_search_too()
    {
        var (tenant, bot, _) = await SeedAsync(("Anything.", FakeEmbeddingService.VectorFor("anything")));

        await using var scope = fixture.OpenScope(tenant.Id, UserRole.Owner);

        (await scope.Send(new SearchBotQuery(bot.Id, Question))).Results.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(UserRole.Viewer)]
    [InlineData(UserRole.Demo)]
    public async Task Viewers_and_the_demo_account_may_not(UserRole role)
    {
        var (tenant, bot, _) = await SeedAsync(("Anything.", FakeEmbeddingService.VectorFor("anything")));

        await using var scope = fixture.OpenScope(tenant.Id, role);

        await Should.ThrowAsync<ForbiddenException>(() => scope.Send(new SearchBotQuery(bot.Id, Question)));
        (await scope.Db.EmbeddingUsages.AnyAsync(u => u.BotId == bot.Id, Ct)).ShouldBeFalse("nothing was searched");
    }

    [Fact]
    public async Task Another_companys_bot_or_an_unknown_one_is_not_found()
    {
        var (_, foreignBot, _) = await SeedAsync(("Secret.", FakeEmbeddingService.VectorFor(Question)));
        var (me, _, _) = await SeedAsync();

        await using var scope = fixture.OpenScope(me.Id);

        await Should.ThrowAsync<NotFoundException>(() => scope.Send(new SearchBotQuery(foreignBot.Id, Question)));
        await Should.ThrowAsync<NotFoundException>(() => scope.Send(new SearchBotQuery(Guid.NewGuid(), Question)));
    }

    [Fact]
    public async Task Nobody_logged_in_is_refused()
    {
        var (_, bot, _) = await SeedAsync();

        await using var scope = fixture.OpenScope(tenantId: null);

        await Should.ThrowAsync<UnauthorizedException>(() => scope.Send(new SearchBotQuery(bot.Id, Question)));
    }

    [Theory]
    [InlineData("", 8)]
    [InlineData("   ", 8)]
    [InlineData(Question, 0)]
    [InlineData(Question, 51)]
    public async Task Refuses_an_empty_question_or_impossible_top_k(string query, int topK)
    {
        var (tenant, bot, _) = await SeedAsync();

        await using var scope = fixture.OpenScope(tenant.Id);

        await Should.ThrowAsync<ValidationException>(() => scope.Send(new SearchBotQuery(bot.Id, query, topK)));
    }

    [Fact]
    public async Task Refuses_a_question_longer_than_1000_characters()
    {
        var (tenant, bot, _) = await SeedAsync();

        await using var scope = fixture.OpenScope(tenant.Id);

        (await scope.Send(new SearchBotQuery(bot.Id, new string('a', 1000)))).Results.ShouldBeEmpty();
        await Should.ThrowAsync<ValidationException>(() => scope.Send(new SearchBotQuery(bot.Id, new string('a', 1001))));
    }

    [Fact]
    public async Task The_api_serves_it_at_bots_id_search()
    {
        await using var api = new ApiFactory(fixture);
        var user = await api.SignUpAsync(Ct);
        var bot = await SeedBotAsync(user.TenantId, ("Employees get 25 vacation days.", FakeEmbeddingService.VectorFor(Question)));
        using var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);

        var response = await client.GetAsync($"/api/bots/{bot.Id}/search?q={Uri.EscapeDataString(Question)}&topK=5", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("query").GetString().ShouldBe(Question);
        body.GetProperty("minSimilarity").GetDouble().ShouldBe(-1);
        body.GetProperty("minKeywordSimilarity").GetDouble().ShouldBe(-0.99); // the API factory's setting
        var result = body.GetProperty("results").EnumerateArray().ShouldHaveSingleItem();
        result.GetProperty("content").GetString().ShouldBe("Employees get 25 vacation days.");
        result.GetProperty("similarity").GetDouble().ShouldBe(1, tolerance: 1e-6);
        result.GetProperty("vectorRank").GetInt32().ShouldBe(1);
        result.TryGetProperty("embedding", out _).ShouldBeFalse("vectors are never sent to the browser");

        (await client.GetAsync($"/api/bots/{bot.Id}/search", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetAsync($"/api/bots/{bot.Id}/search?q=x&topK=99", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetAsync($"/api/bots/{Guid.NewGuid()}/search?q=x", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        client.DefaultRequestHeaders.Authorization = null;
        (await client.GetAsync($"/api/bots/{bot.Id}/search?q=x", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_api_says_the_ai_service_is_unavailable_when_embedding_fails()
    {
        await using var api = new ApiFactory(fixture);
        var user = await api.SignUpAsync(Ct);
        var bot = await SeedBotAsync(user.TenantId);
        using var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);

        HttpResponseMessage response;
        using (fixture.Embeddings.Intercept((_, _) => throw new EmbeddingException("Voyage AI rate limit reached (429).")))
        {
            response = await client.GetAsync($"/api/bots/{bot.Id}/search?q=refund", Ct);
        }

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("title").GetString().ShouldBe("The AI service is not available right now.");
        problem.GetProperty("detail").GetString().ShouldBe("Please try again in a minute.");
    }

    // ---------- helpers ----------

    private async Task<(Tenant Tenant, Bot Bot, IReadOnlyList<Chunk> Chunks)> SeedAsync(
        params (string Content, float[] Embedding)[] chunks)
    {
        var tenant = Tenant.Create("Search company");
        tenant.ChangeSlug($"search-{Guid.NewGuid():N}");
        await using (var scope = fixture.OpenScope(tenantId: null))
        {
            scope.Db.Tenants.Add(tenant);
            await scope.Db.SaveChangesAsync(Ct);
        }

        var bot = await SeedBotAsync(tenant.Id, chunks);
        await using var read = fixture.OpenScope(tenantId: null);
        var saved = await read.Db.Chunks.Where(c => c.BotId == bot.Id).OrderBy(c => c.Index).ToListAsync(Ct);
        return (tenant, bot, saved);
    }

    /// <summary>A bot with one Ready PDF whose chunks are on pages 2, 3, … (heading "Benefits").</summary>
    private async Task<Bot> SeedBotAsync(Guid tenantId, params (string Content, float[] Embedding)[] chunks)
    {
        var bot = Bot.Create(tenantId, "Support");
        var document = Document.FromUpload(
            tenantId, bot.Id, "Handbook.pdf", "application/pdf", 100, DocumentSourceType.Pdf,
            $"{tenantId}/{bot.Id}/{Guid.NewGuid():N}/original.pdf", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
        var entities = chunks
            .Select((c, i) =>
            {
                var chunk = Chunk.Create(document.Id, bot.Id, i, c.Content, "Benefits", i + 2, 10, 10 + c.Content.Length, 8);
                chunk.SetEmbedding(c.Embedding);
                return chunk;
            })
            .ToList();

        await using var scope = fixture.OpenScope(tenantId: null);
        scope.Db.AddRange(bot, document);
        scope.Db.Chunks.AddRange(entities);
        await scope.Db.SaveChangesAsync(Ct);
        return bot;
    }
}
