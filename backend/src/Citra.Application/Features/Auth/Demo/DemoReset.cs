using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Conversations;
using Citra.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Auth.Demo;

/// <summary>Cleans up the public demo; the Worker runs it every night.</summary>
public interface IDemoReset
{
    /// <summary>Removes what visitors left: their demo users with their chats, and website chats. False without a demo.</summary>
    Task<DemoResetResult> RunAsync(CancellationToken cancellationToken);
}

public sealed record DemoResetResult(bool HasDemo, int DemoUsers, int Conversations);

/// <summary>
/// Visitors cannot change the demo company's bots, documents or test sets (they are read-only), so only what they
/// leave behind is removed: the demo user of every visit with its chats, and website-widget chats. Anything from the
/// last hour stays, so nobody in the middle of a visit is thrown out. There is no user in the background, so the
/// company filter is lifted: the demo company is named by its slug.
/// </summary>
internal sealed class DemoReset(
    IApplicationDbContext db,
    IIdentityService identity,
    IDemoSettings settings,
    TimeProvider timeProvider) : IDemoReset
{
    public static readonly TimeSpan KeepRecent = TimeSpan.FromHours(1);

    public async Task<DemoResetResult> RunAsync(CancellationToken cancellationToken)
    {
        var tenantId = string.IsNullOrWhiteSpace(settings.TenantSlug)
            ? null
            : await db.Tenants.Where(t => t.Slug == settings.TenantSlug).Select(t => (Guid?)t.Id).SingleOrDefaultAsync(cancellationToken);
        if (tenantId is not { } demo)
        {
            return new DemoResetResult(false, 0, 0);
        }

        var before = timeProvider.GetUtcNow() - KeepRecent;
        var visitors = await identity.FindUserIdsAsync(demo, UserRole.Demo, before, cancellationToken);

        // Chats first: deleting a user would only unlink its chats. Their messages go with them (ON DELETE CASCADE).
        var conversations = await db.Conversations.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(c => c.TenantId == demo
                && ((c.UserId != null && visitors.Contains(c.UserId.Value))
                    || (c.Channel == ConversationChannel.Widget && c.UpdatedAt < before)))
            .ExecuteDeleteAsync(cancellationToken);
        await identity.DeleteUsersAsync(visitors, cancellationToken);

        return new DemoResetResult(true, visitors.Count, conversations);
    }
}
