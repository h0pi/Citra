namespace Citra.Domain.Conversations;

public enum ConversationChannel
{
    /// <summary>Chat page inside the admin app (logged-in user).</summary>
    Admin = 0,

    /// <summary>Public website widget (anonymous visitor).</summary>
    Widget = 1,
}
