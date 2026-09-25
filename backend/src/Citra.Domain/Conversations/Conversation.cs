using Citra.Domain.Common;

namespace Citra.Domain.Conversations;

/// <summary>A chat thread between a user (or website visitor) and a bot.</summary>
public sealed class Conversation : Entity, ITenantOwned
{
    public const int TitleMaxLength = 200;
    public const int VisitorIdMaxLength = 64;
    public const string DefaultTitle = "New chat";

    private readonly List<Message> _messages = [];

    public Guid TenantId { get; private set; }

    public Guid BotId { get; private set; }

    public ConversationChannel Channel { get; private set; }

    /// <summary>Set for admin-app chats: the logged-in user.</summary>
    public Guid? UserId { get; private set; }

    /// <summary>Set for widget chats: random id stored in the visitor's browser.</summary>
    public string? VisitorId { get; private set; }

    public string Title { get; private set; } = DefaultTitle;

    public IReadOnlyList<Message> Messages => _messages;

    private Conversation()
    {
        // For EF Core.
    }

    public static Conversation StartInAdmin(Guid tenantId, Guid botId, Guid userId)
    {
        EnsureIds(tenantId, botId);
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        return new Conversation
        {
            TenantId = tenantId,
            BotId = botId,
            Channel = ConversationChannel.Admin,
            UserId = userId,
        };
    }

    public static Conversation StartInWidget(Guid tenantId, Guid botId, string visitorId)
    {
        EnsureIds(tenantId, botId);
        ArgumentException.ThrowIfNullOrWhiteSpace(visitorId);
        if (visitorId.Length > VisitorIdMaxLength)
        {
            throw new ArgumentException("Visitor id is too long.", nameof(visitorId));
        }

        return new Conversation
        {
            TenantId = tenantId,
            BotId = botId,
            Channel = ConversationChannel.Widget,
            VisitorId = visitorId,
        };
    }

    public void Rename(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var trimmed = title.Trim();
        Title = trimmed.Length > TitleMaxLength ? trimmed[..TitleMaxLength] : trimmed;
    }

    public Message AddUserMessage(string content)
    {
        var message = Message.FromUser(Id, content);
        _messages.Add(message);
        return message;
    }

    public Message AddAssistantMessage(string content)
    {
        var message = Message.FromAssistant(Id, content);
        _messages.Add(message);
        return message;
    }

    private static void EnsureIds(Guid tenantId, Guid botId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (botId == Guid.Empty)
        {
            throw new ArgumentException("Bot id is required.", nameof(botId));
        }
    }
}
