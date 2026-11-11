using System.Text;
using Citra.Application.Common.Chat;
using Citra.Application.Common.Embeddings;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Retrieval;
using Citra.Application.Features.Bots;
using Citra.Application.Features.Chat.SendMessage;
using Citra.Domain.Bots;
using Citra.Domain.Usage;

namespace Citra.Application.Features.Evaluation.Runs;

/// <summary>
/// A test question's answer, as a chat would give it: the bot's search, prompt and model.
/// </summary>
/// <param name="SourceDocumentIds">Documents of the passages the search found, best first.</param>
/// <param name="CostUsd">Model and search cost together.</param>
internal sealed record EvalAnswer(string Answer, bool WasAnswered, IReadOnlyList<Guid> SourceDocumentIds, decimal CostUsd, int LatencyMs);

/// <summary>
/// Answers one test question like a new chat would (search the bot's documents → ask the model with the numbered
/// sources), but saves no conversation. The search's cost is recorded like any other (it was billed).
/// </summary>
internal sealed class EvalAnswerer(
    IApplicationDbContext db,
    IRetriever retriever,
    IEmbeddingService embeddings,
    IChatModel model,
    IChatSettings settings,
    IChatPricing pricing,
    TimeProvider timeProvider)
{
    public async Task<EvalAnswer> AnswerAsync(Bot bot, string question, CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetTimestamp();

        var found = await retriever.SearchAsync(bot.Id, question, settings.SourcesPerAnswer, cancellationToken);
        var searchCost = EmbeddingCost.Calculate(found.QueryTokens, embeddings.PricePerMillionTokensUsd);
        db.EmbeddingUsages.Add(EmbeddingUsage.ForQuery(bot.TenantId, bot.Id, embeddings.Model, found.QueryTokens, searchCost));

        var sources = SourceList.Number(found.Chunks);
        var request = new ChatRequest(
            ChatPrompts.System(bot.Name, bot.SystemPrompt),
            [new ChatMessage(ChatRole.User, ChatPrompts.UserTurn(sources, question))])
        {
            Model = BotModel(bot),
        };

        var answer = new StringBuilder();
        ChatUsage? usage = null;
        await foreach (var chatEvent in model.StreamAsync(request, cancellationToken))
        {
            switch (chatEvent)
            {
                case ChatTextDelta delta:
                    answer.Append(delta.Text);
                    break;
                case ChatCompleted completed:
                    usage = completed.Usage;
                    break;
            }
        }

        var content = NotFoundFilter.Clean(answer.ToString(), out var notFound);
        return new EvalAnswer(
            content.Length > 0 ? content : SendMessageCommandHandler.NotFoundFallback,
            !notFound,
            [.. found.Chunks.Select(c => c.DocumentId).Distinct()],
            (usage is null ? 0 : pricing.Cost(usage)) + searchCost,
            (int)timeProvider.GetElapsedTime(startedAt).TotalMilliseconds);
    }

    /// <summary>The bot's model, or null for the app default (also when the saved one is no longer offered).</summary>
    public static string? BotModel(Bot bot) => bot.Model is { } chosen && BotModels.IsSupported(chosen) ? chosen : null;
}
