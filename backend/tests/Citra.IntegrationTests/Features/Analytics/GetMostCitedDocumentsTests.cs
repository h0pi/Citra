using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Analytics;
using Citra.Domain.Conversations;
using Citra.Domain.Documents;
using Citra.IntegrationTests.Features.Chat;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Citra.IntegrationTests.Features.Analytics.AnalyticsData;
using static Citra.IntegrationTests.Features.Chat.ChatData;

namespace Citra.IntegrationTests.Features.Analytics;

/// <summary>The dashboard's "most cited" list: documents ranked by how many answers cited them.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class GetMostCitedDocumentsTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ranks_documents_by_answers_that_cited_them()
    {
        var seed = await SeedAsync(fixture, []);
        var returns = await AddDocumentAsync(seed, seed.Bot.Id, "Returns.pdf");
        var shipping = await AddDocumentAsync(seed, seed.Bot.Id, "Shipping.pdf");
        var deleted = await AddDocumentAsync(seed, seed.Bot.Id, "Old.pdf");
        await CitingChatAsync(seed, seed.Bot.Id, [returns, returns, shipping]); // one answer citing Returns twice
        await CitingChatAsync(seed, seed.Bot.Id, [returns]);
        await CitingChatAsync(seed, seed.Bot.Id, [deleted]);
        await MoveBackAsync(fixture, await CitingChatAsync(seed, seed.Bot.Id, [shipping], answers: 5), days: 10);
        var otherBot = await AddBotAsync(fixture, seed.Tenant.Id);
        var faq = await AddDocumentAsync(seed, otherBot.Id, "Faq.pdf");
        await CitingChatAsync(seed, otherBot.Id, [faq], answers: 3);
        var stranger = await SeedAsync(fixture, []);
        await CitingChatAsync(stranger, stranger.Bot.Id, [await AddDocumentAsync(stranger, stranger.Bot.Id, "Theirs.pdf")], answers: 9);
        await using (var scope = fixture.OpenScope(seed.Tenant.Id))
        {
            await scope.Db.Documents.Where(d => d.Id == deleted).ExecuteDeleteAsync(Ct);
        }

        await using var query = Scope(fixture, seed);
        var bot = await query.Send(new GetMostCitedDocumentsQuery(seed.Bot.Id, Today.AddDays(-6), Today));
        var all = await query.Send(new GetMostCitedDocumentsQuery(null, Today, Today, Limit: 2));

        bot.ShouldBe([new CitedDocumentDto(returns, "Returns.pdf", 2), new CitedDocumentDto(shipping, "Shipping.pdf", 1)]);
        all.ShouldBe([new CitedDocumentDto(faq, "Faq.pdf", 3), new CitedDocumentDto(returns, "Returns.pdf", 2)]);
    }

    [Fact]
    public async Task The_limit_and_the_period_are_checked()
    {
        var seed = await SeedAsync(fixture, []);

        await using var scope = Scope(fixture, seed);
        await Should.ThrowAsync<ValidationException>(() => scope.Send(new GetMostCitedDocumentsQuery(null, Today, Today, Limit: 0)));
        await Should.ThrowAsync<ValidationException>(() => scope.Send(new GetMostCitedDocumentsQuery(null, Today, Today, Limit: 51)));
        await Should.ThrowAsync<ValidationException>(() => scope.Send(new GetMostCitedDocumentsQuery(null, Today, Today.AddDays(-1))));
    }

    private async Task<Guid> AddDocumentAsync(ChatSeed seed, Guid botId, string name)
    {
        var document = Document.FromUpload(
            seed.Tenant.Id, botId, name, "application/pdf", 100, DocumentSourceType.Pdf,
            $"{seed.Tenant.Id}/{botId}/{Guid.NewGuid():N}/original.pdf", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
        await using var scope = fixture.OpenScope(seed.Tenant.Id);
        scope.Db.Documents.Add(document);
        await scope.Db.SaveChangesAsync(Ct);
        return document.Id;
    }

    /// <summary>A chat whose every answer cites the given documents, as [1], [2], ...</summary>
    private async Task<Guid> CitingChatAsync(ChatSeed seed, Guid botId, IReadOnlyList<Guid> documents, int answers = 1)
    {
        await using var scope = fixture.OpenScope(seed.Tenant.Id);
        var names = await scope.Db.Documents.Where(d => documents.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.FileName, Ct);
        var conversation = Conversation.StartInAdmin(seed.Tenant.Id, botId, seed.UserId);
        for (var i = 0; i < answers; i++)
        {
            conversation.AddUserMessage("Question?");
            var answer = conversation.AddAssistantMessage("...");
            answer.CompleteAnswer("Answer.", true);
            for (var n = 0; n < documents.Count; n++)
            {
                answer.AddCitation(n + 1, null, documents[n], names[documents[n]], 1, "Snippet.", 0.9);
            }
        }

        scope.Db.Conversations.Add(conversation);
        await scope.Db.SaveChangesAsync(Ct);
        return conversation.Id;
    }
}
