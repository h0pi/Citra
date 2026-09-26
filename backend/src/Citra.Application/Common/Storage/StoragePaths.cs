namespace Citra.Application.Common.Storage;

/// <summary>
/// The storage layout: one folder per tenant, then per bot, then per document.
/// Deleting a document, a bot or a whole tenant is therefore a single folder delete.
/// <code>
/// {tenantId}/
///   {botId}/
///     {documentId}/
///       original.pdf     ← the uploaded file
/// </code>
/// The user's file name is never part of a path (it stays in the database for display), so names like
/// "../../x" or "CON.pdf" cannot cause trouble; only a cleaned-up extension is kept.
/// </summary>
public static class StoragePaths
{
    /// <summary>Longest extension kept, including the dot (".markdown" = 9).</summary>
    private const int MaxExtensionLength = 10;

    public static string BotFolder(Guid tenantId, Guid botId) => $"{tenantId}/{botId}";

    public static string DocumentFolder(Guid tenantId, Guid botId, Guid documentId) =>
        $"{BotFolder(tenantId, botId)}/{documentId}";

    /// <summary>Where a document's uploaded file is stored, e.g. ".../{documentId}/original.pdf".</summary>
    public static string OriginalFile(Guid tenantId, Guid botId, Guid documentId, string fileName) =>
        $"{DocumentFolder(tenantId, botId, documentId)}/original{SafeExtension(fileName)}";

    /// <summary>
    /// ".PDF" → ".pdf"; anything that is not a plain short extension of letters/digits becomes "" (no extension).
    /// </summary>
    public static string SafeExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        var valid = extension.Length is > 1 and <= MaxExtensionLength
            && extension[1..].All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9');
        return valid ? extension : string.Empty;
    }
}
