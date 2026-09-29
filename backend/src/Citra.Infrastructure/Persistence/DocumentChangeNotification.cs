using System.Text.Json;

namespace Citra.Infrastructure.Persistence;

/// <summary>
/// A PostgreSQL notification that a document changed, sent by the documents table's trigger (migration
/// AddDocumentChangeNotifications) when the change is committed. It only says which document; readers load its
/// current state themselves, so a burst of quick changes can never show an outdated one.
/// </summary>
public sealed record DocumentChangeNotification(Guid DocumentId, Guid TenantId, Guid BotId, bool Deleted)
{
    /// <summary>The LISTEN/NOTIFY channel (the name used in the trigger).</summary>
    public const string Channel = "document_changes";

    /// <summary>Reads the trigger's JSON payload; null if it is not one (never expected, but never trusted blindly).</summary>
    public static DocumentChangeNotification? Parse(string payload)
    {
        try
        {
            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;
            return new DocumentChangeNotification(
                root.GetProperty("id").GetGuid(),
                root.GetProperty("tenant_id").GetGuid(),
                root.GetProperty("bot_id").GetGuid(),
                root.GetProperty("deleted").GetBoolean());
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
