using Citra.Application.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Chat.Conversations;

/// <summary>How long chats are kept (section "Retention"); no value = forever.</summary>
public interface IRetentionSettings
{
    /// <summary>Chats nobody used for longer than this many days are deleted, with their messages.</summary>
    int? ConversationDays { get; }
}

/// <summary>Deletes chats past the retention period; the Worker runs it once a day.</summary>
public interface IConversationCleanup
{
    /// <summary>Deletes the expired chats of every company. Returns how many; 0 when chats are kept forever.</summary>
    Task<int> DeleteExpiredAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Chats (admin and website) last used before the retention period are deleted, for every company: there is no user
/// in the background, so the company filter is lifted. Their messages and citations go with them (ON DELETE CASCADE).
/// The dashboard then no longer counts them.
/// </summary>
internal sealed class ConversationCleanup(
    IApplicationDbContext db,
    IRetentionSettings settings,
    TimeProvider timeProvider) : IConversationCleanup
{
    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        if (settings.ConversationDays is not { } days)
        {
            return 0;
        }

        var cutoff = timeProvider.GetUtcNow().AddDays(-days);
        return await db.Conversations.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(c => c.UpdatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
