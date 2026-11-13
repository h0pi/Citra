using Citra.Application.Common.Retrieval;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using static Citra.IntegrationTests.Retrieval.SearchData;

namespace Citra.IntegrationTests.Retrieval;

/// <summary>Keyword search with PostgreSQL full-text search — any of the question's words, best first.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class KeywordSearchTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Finds_chunks_containing_the_questions_words_in_any_form()
    {
        var seed = await SeedAsync(fixture, [Text("Parking is free for employees."), Text("Refunds are processed within 14 days.")]);

        // "refund" finds "Refunds" (stemming); "how", "do", "I", "a" are common words and ignored.
        var found = await KeywordAsync(seed.Bot.Id, "How do I get a refund?");

        var hit = found.ShouldHaveSingleItem();
        hit.Content.ShouldBe("Refunds are processed within 14 days.");
        hit.KeywordScore!.Value.ShouldBeGreaterThan(0);
        hit.Similarity.ShouldBeNull();
    }

    [Fact]
    public async Task One_word_is_enough_and_chunks_with_more_of_the_words_rank_higher()
    {
        var seed = await SeedAsync(
            fixture,
            [Text("Sick days need a doctor's note."), Text("Parking is free."), Text("Unused vacation days carry over.")]);

        var found = await KeywordAsync(seed.Bot.Id, "vacation days");

        found.Select(c => c.Content).ShouldBe(["Unused vacation days carry over.", "Sick days need a doctor's note."]);
        found[0].KeywordScore!.Value.ShouldBeGreaterThan(found[1].KeywordScore!.Value);
    }

    [Fact]
    public async Task A_match_in_the_heading_ranks_above_one_in_the_text()
    {
        var seed = await SeedAsync(
            fixture,
            [
                Text("Some teams plan their vacation early.", heading: "Planning"),
                Text("Everyone gets 25 days per year.", heading: "Vacation"),
            ]);

        var found = await KeywordAsync(seed.Bot.Id, "vacation");

        found.Select(c => c.Heading).ShouldBe(["Vacation", "Planning"]);
    }

    [Fact]
    public async Task Codes_and_numbers_match_exactly_first()
    {
        var seed = await SeedAsync(
            fixture,
            [Text("Error ERR-4013 means the card expired."), Text("Error ERR-4012 means the card was declined.")]);

        var found = await KeywordAsync(seed.Bot.Id, "What is ERR-4012?");

        found[0].Content.ShouldBe("Error ERR-4012 means the card was declined.");
    }

    [Fact]
    public async Task Punctuation_and_search_operators_in_the_question_are_just_text()
    {
        var seed = await SeedAsync(fixture, [Text("Refunds are processed within 14 days.")]);

        var found = await KeywordAsync(seed.Bot.Id, "refund's & (policy) | !x:* 'quoted' \\ \"why\" <-> ?");

        found.ShouldHaveSingleItem().Content.ShouldBe("Refunds are processed within 14 days.");
    }

    [Fact]
    public async Task Web_addresses_in_the_question_are_searchable_words_too()
    {
        // PostgreSQL keeps "example.com:8080/refunds" as one word: its ':' must not be read as search syntax.
        var seed = await SeedAsync(fixture, [Text("Send refund requests to https://example.com:8080/refunds today.")]);

        var found = await KeywordAsync(seed.Bot.Id, "Is https://example.com:8080/refunds the right page?");

        found.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("what is the")]
    [InlineData("?!")]
    public async Task A_question_of_only_common_words_or_symbols_finds_nothing(string query)
    {
        var seed = await SeedAsync(fixture, [Text("What is the policy? The answer is here.")]);

        (await KeywordAsync(seed.Bot.Id, query)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Searches_only_the_given_bots_chunks()
    {
        var mine = await SeedAsync(fixture, [Text("Our refund policy.")]);
        await SeedAsync(fixture, [Text("Refund refund refund, another company.")]);

        var found = await KeywordAsync(mine.Bot.Id, "refund");

        found.Select(c => c.Content).ShouldBe(["Our refund policy."]);
    }

    [Fact]
    public async Task Returns_at_most_the_limit_best_first()
    {
        var seed = await SeedAsync(
            fixture,
            [Text("refund"), Text("refund policy and refund fees"), Text("refund policy"), Text("parking")]);

        var found = await KeywordAsync(seed.Bot.Id, "refund policy fees", limit: 2);

        found.Select(c => c.Content).ShouldBe(["refund policy and refund fees", "refund policy"]);
    }

    [Fact]
    public async Task Returns_what_a_citation_needs()
    {
        var seed = await SeedAsync(
            fixture,
            [new("Employees get 25 days of paid vacation.", null, PageNumber: 4, Heading: "Benefits", CharStart: 120)],
            documentName: "Employee handbook.pdf");

        var hit = (await KeywordAsync(seed.Bot.Id, "vacation")).ShouldHaveSingleItem();

        var chunk = seed.Chunks[0];
        hit.ShouldBe(new ChunkHit(
            chunk.Id, seed.Document.Id, "Employee handbook.pdf", 0, 4, "Benefits",
            "Employees get 25 days of paid vacation.", 120, 120 + chunk.Content.Length, Similarity: null, hit.KeywordScore));
    }

    [Theory]
    [InlineData(" ", 5)]
    [InlineData("refund", 0)]
    public async Task Rejects_a_blank_query_or_bad_limit(string query, int limit)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        var search = scope.Services.GetRequiredService<IChunkSearch>();

        await Should.ThrowAsync<ArgumentException>(() => search.KeywordAsync(Guid.NewGuid(), query, limit, Ct));
    }

    /// <summary>A chunk for keyword search (no embedding: keyword search does not need one).</summary>
    private static SeedChunk Text(string content, string? heading = null) => new(content, null, Heading: heading);

    private async Task<IReadOnlyList<ChunkHit>> KeywordAsync(Guid botId, string query, int limit = 10)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        return await scope.Services.GetRequiredService<IChunkSearch>().KeywordAsync(botId, query, limit, Ct);
    }
}
