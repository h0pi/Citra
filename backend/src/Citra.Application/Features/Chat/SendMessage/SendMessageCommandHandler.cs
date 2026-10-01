using System.Runtime.CompilerServices;
using System.Text;
using Citra.Application.Common.Chat;
using Citra.Application.Common.Embeddings;
using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Retrieval;
using Citra.Application.Common.Security;
using Citra.Application.Features.Bots;
using Citra.Domain.Bots;
using Citra.Domain.Conversations;
using Citra.Domain.Usage;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Citra.Application.Features.Chat.SendMessage;

/// <summary>
/// save the question → rewrite a follow-up into a standalone question → search the bot's documents → ask the
/// model with the numbered sources → stream the answer → save it with the sources it cites, whether the documents
/// had the answer, and what it cost. Any role may chat (viewers and the demo account too); admin-app chats are
/// private to the user who started them.
/// </summary>
internal sealed class SendMessageCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IQueryRewriter rewriter,
    IRetriever retriever,
    IEmbeddingService embeddings,
    IChatModel model,
    IChatSettings settings,
    Common.Chunking.ITokenCounter tokens,
    IChatPricing pricing,
    TimeProvider timeProvider,
    ILogger<SendMessageCommandHandler> logger) : IStreamRequestHandler<SendMessageCommand, SendMessageEvent>
{
    /// <summary>Saved when the model only wrote the "not found" marker.</summary>
    internal const string NotFoundFallback = "I could not find this in the documents.";

    public async IAsyncEnumerable<SendMessageEvent> Handle(
        SendMessageCommand request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetTimestamp(); // latency: from the question to the saved answer
        var tenantId = currentUser.RequireTenantId();
        var userId = currentUser.RequireUserId();
        var question = request.Question.Trim();

        // Another company's bot is hidden by the tenant filter: 404, the retriever is never reached with it.
        var bot = await db.Bots.AsNoTracking().SingleOrDefaultAsync(b => b.Id == request.BotId, cancellationToken)
            ?? throw new NotFoundException(nameof(Bot), request.BotId);

        var conversation = await ConversationAsync(request, bot, tenantId, userId, cancellationToken);
        IReadOnlyList<ChatMessage> history = request.ConversationId is null
            ? []
            : await HistoryAsync(conversation.Id, cancellationToken);

        // The question is kept even if answering fails below (it shows what people asked).
        var questionMessage = conversation.AddUserMessage(question);
        await db.SaveChangesAsync(cancellationToken);

        var rewritten = await rewriter.RewriteAsync(history, question, cancellationToken);
        var found = await retriever.SearchAsync(bot.Id, rewritten.Query, settings.SourcesPerAnswer, cancellationToken);

        // Voyage billed the search: record it now, whatever happens to the answer (and even if the user left).
        db.EmbeddingUsages.Add(EmbeddingUsage.ForQuery(
            tenantId, bot.Id, embeddings.Model, found.QueryTokens,
            EmbeddingCost.Calculate(found.QueryTokens, embeddings.PricePerMillionTokensUsd)));
        await db.SaveChangesAsync(CancellationToken.None);

        yield return new MessageStarted(conversation.Id, questionMessage.Id, rewritten.Query);

        var sources = SourceList.Number(found.Chunks);
        yield return new SourcesFound([.. sources.Sources.Select(s => new AnswerSource(
            s.Number, s.Chunk.ChunkId, s.Chunk.DocumentId, s.Chunk.DocumentName, s.Chunk.PageNumber, s.Chunk.Heading,
            s.Chunk.Content, s.Chunk.CharStart, s.Chunk.CharEnd, s.Chunk.Score))]);
        var chatRequest = new ChatRequest(
            ChatPrompts.System(bot.Name, bot.SystemPrompt),
            [
                .. ChatHistory.Trim(history, settings.MaxHistoryMessages, settings.MaxHistoryTokens, tokens),
                new ChatMessage(ChatRole.User, ChatPrompts.UserTurn(sources, question)),
            ])
        {
            // A model saved before it stopped being offered falls back to the app default.
            Model = bot.Model is { } chosen && BotModels.IsSupported(chosen) ? chosen : null,
            Temperature = bot.Temperature,
        };

        var answer = new StringBuilder();
        var notFoundMarker = new NotFoundFilter(); // the "not found" marker is never shown
        ChatUsage? usage = null;
        await foreach (var chatEvent in model.StreamAsync(chatRequest, cancellationToken))
        {
            switch (chatEvent)
            {
                case ChatTextDelta delta:
                    answer.Append(delta.Text);
                    if (notFoundMarker.Push(delta.Text) is { Length: > 0 } visible)
                    {
                        yield return new AnswerToken(visible);
                    }

                    break;

                case ChatCompleted completed:
                    usage = completed.Usage;
                    break;
            }
        }

        if (notFoundMarker.Finish() is { Length: > 0 } held)
        {
            yield return new AnswerToken(held);
        }

        if (string.IsNullOrWhiteSpace(answer.ToString()))
        {
            throw new ChatModelException("The model returned an empty answer.");
        }

        var content = NotFoundFilter.Clean(answer.ToString(), out var notFound);
        var answerMessage = conversation.AddAssistantMessage(string.Empty);
        answerMessage.CompleteAnswer(content.Length > 0 ? content : NotFoundFallback, wasAnswered: !notFound);

        // Only the sources the answer actually cites are kept (a "not found" answer cites none).
        IReadOnlyList<NumberedSource> cited = notFound ? [] : CitationMarkers.Cited(answerMessage.Content, sources);
        foreach (var source in cited)
        {
            answerMessage.AddCitation(
                source.Number, source.Chunk.ChunkId, source.Chunk.DocumentId, source.Chunk.DocumentName,
                source.Chunk.PageNumber, source.Chunk.Content, source.Chunk.Score);
        }

        // Tokens and cost of both model calls (rewriting the question and answering); the search's cost is
        // recorded separately (EmbeddingUsage).
        if (usage is null)
        {
            logger.LogWarning("The chat model reported no token usage for message in conversation {ConversationId}", conversation.Id);
        }

        ChatUsage[] calls = [.. new[] { rewritten.Usage, usage }.OfType<ChatUsage>()];
        answerMessage.RecordUsage(
            calls.Sum(c => c.InputTokens),
            calls.Sum(c => c.OutputTokens),
            calls.Sum(pricing.Cost),
            (int)timeProvider.GetElapsedTime(startedAt).TotalMilliseconds);
        conversation.UpdatedAt = timeProvider.GetUtcNow(); // newest activity first in the chat list

        // The answer was written (and paid for): save it even if the user closed the page at the last moment.
        await db.SaveChangesAsync(CancellationToken.None);

        yield return new AnswerCompleted(
            answerMessage.Id, answerMessage.Content, answerMessage.WasAnswered, [.. cited.Select(s => s.Number)]);
    }

    /// <summary>A new conversation, or the user's own existing one with this bot (anything else is a 404).</summary>
    private async Task<Conversation> ConversationAsync(
        SendMessageCommand request, Bot bot, Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        if (request.ConversationId is not { } id)
        {
            var started = Conversation.StartInAdmin(tenantId, bot.Id, userId);
            started.Rename(ConversationTitle.FromQuestion(request.Question));
            db.Conversations.Add(started);
            return started;
        }

        var conversation = await db.Conversations.SingleOrDefaultAsync(
                c => c.Id == id && c.BotId == bot.Id && c.UserId == userId && c.Channel == ConversationChannel.Admin,
                cancellationToken)
            ?? throw new NotFoundException(nameof(Conversation), id);

        conversation.UpdatedAt = timeProvider.GetUtcNow(); // newest activity first in the chat list
        return conversation;
    }

    /// <summary>
    /// The earlier questions and answers, oldest first. A question whose answer failed (no answer was saved after
    /// it) is left out: the model would otherwise see two questions in a row and might answer the old one.
    /// </summary>
    private async Task<IReadOnlyList<ChatMessage>> HistoryAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var messages = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Role == MessageRole.User ? 0 : 1)
            .Select(m => new { m.Role, m.Content })
            .ToListAsync(cancellationToken);

        var history = new List<ChatMessage>();
        for (var i = 0; i < messages.Count - 1; i++)
        {
            if (messages[i].Role == MessageRole.User && messages[i + 1].Role == MessageRole.Assistant)
            {
                history.Add(new ChatMessage(ChatRole.User, messages[i].Content));
                history.Add(new ChatMessage(ChatRole.Assistant, messages[i + 1].Content));
                i++;
            }
        }

        return history;
    }
}
