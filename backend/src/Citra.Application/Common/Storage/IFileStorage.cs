namespace Citra.Application.Common.Storage;

/// <summary>
/// Where uploaded document files live (local disk now, cloud storage later).
/// Paths are relative, use '/', and are built only with <see cref="StoragePaths"/>, never from user input,
/// e.g. "{tenantId}/{botId}/{documentId}/original.pdf".
/// Implementations must refuse paths that point outside their storage root.
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="path"/>, creating folders as needed and replacing
    /// an existing file. The file appears only once it is completely written, so a failed or cancelled
    /// upload never leaves a half-written file behind. Returns the number of bytes written.
    /// </summary>
    Task<long> SaveAsync(string path, Stream content, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a stored file for reading (the caller disposes the stream), or returns null if it does not exist,
    /// e.g. because its document was deleted in the meantime.
    /// </summary>
    Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken);

    /// <summary>Deletes one file. Does nothing if it does not exist.</summary>
    Task DeleteAsync(string path, CancellationToken cancellationToken);

    /// <summary>Deletes a folder and everything inside it. Does nothing if the folder does not exist.</summary>
    Task DeleteFolderAsync(string folder, CancellationToken cancellationToken);
}
