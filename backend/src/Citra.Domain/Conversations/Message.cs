using Citra.Domain.Common;

namespace Citra.Domain.Conversations;

/// <summary>
/// One question or answer in a conversation. Assistant messages also record usage
/// (tokens, cost, latency) for the analytics dashboard, and the sources they cited.
/// </summary>
public sealed class Message : Entity
{
    public const int ContentMaxLength = 20_000;

    private readonly List<MessageCitation> _citations = [];

    public Guid ConversationId { get; private set; }

    public MessageRole Role { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public int InputTokens { get; private set; }

    public int OutputTokens { get; private set; }

    public decimal CostUsd { get; private set; }

    /// <summary>Time from question to complete answer.</summary>
    public int LatencyMs { get; private set; }

    /// <summary>False when the bot could not find the answer in the documents ("knowledge gap").</summary>
    public bool WasAnswered { get; private set; } = true;

    public MessageFeedback Feedback { get; private set; } = MessageFeedback.None;

    public IReadOnlyList<MessageCitation> Citations => _citations;

    private Message()
    {
        // For EF Core.
    }

    internal static Message FromUser(Guid conversationId, string content) =>
        Create(conversationId, MessageRole.User, content);

    internal static Message FromAssistant(Guid conversationId, string content) =>
        Create(conversationId, MessageRole.Assistant, content);

    /// <summary>Fills in the answer once streaming has finished.</summary>
    public void CompleteAnswer(string content, bool wasAnswered)
    {
        EnsureAssistant();
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        Content = Truncate(content);
        WasAnswered = wasAnswered;
    }

    public void RecordUsage(int inputTokens, int outputTokens, decimal costUsd, int latencyMs)
    {
        EnsureAssistant();
        ArgumentOutOfRangeException.ThrowIfNegative(inputTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(outputTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(costUsd);
        ArgumentOutOfRangeException.ThrowIfNegative(latencyMs);

        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CostUsd = costUsd;
        LatencyMs = latencyMs;
    }

    public MessageCitation AddCitation(
        int number,
        Guid? chunkId,
        Guid? documentId,
        string documentName,
        int? pageNumber,
        string snippet,
        double score)
    {
        EnsureAssistant();
        if (_citations.Any(c => c.Number == number))
        {
            throw new InvalidOperationException($"Citation [{number}] already exists on message {Id}.");
        }

        var citation = MessageCitation.Create(Id, number, chunkId, documentId, documentName, pageNumber, snippet, score);
        _citations.Add(citation);
        return citation;
    }

    public void GiveFeedback(MessageFeedback feedback)
    {
        EnsureAssistant();
        Feedback = feedback;
    }

    private static Message Create(Guid conversationId, MessageRole role, string content)
    {
        // Assistant messages start empty and are filled when the streamed answer completes.
        if (role == MessageRole.User)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(content);
        }

        return new Message
        {
            ConversationId = conversationId,
            Role = role,
            Content = Truncate(content ?? string.Empty),
        };
    }

    private void EnsureAssistant()
    {
        if (Role != MessageRole.Assistant)
        {
            throw new InvalidOperationException("Only assistant messages have answers, usage, citations and feedback.");
        }
    }

    private static string Truncate(string content) =>
        content.Length > ContentMaxLength ? content[..ContentMaxLength] : content;
}
