using Citra.Application.Common.Storage;
using Citra.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Citra.Infrastructure.Storage;

/// <summary>
/// Stores files in a folder on this machine (Storage:LocalRootPath). Api and Worker are configured
/// with the same folder, so the Worker can read what the Api saved.
/// </summary>
internal sealed class LocalFileStorage(IOptions<StorageOptions> options) : IFileStorage
{
    // Relative roots resolve from the current directory, which is the project folder under dotnet run and VS.
    private readonly string _root = Path.GetFullPath(options.Value.LocalRootPath);

    // TODO(C-102): implement Save/Open/Delete on disk. Placeholders only while the interface is defined (C-101).
    public Task<long> SaveAsync(string path, Stream content, CancellationToken cancellationToken) =>
        throw new NotImplementedException("C-102");

    public Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken) =>
        throw new NotImplementedException("C-102");

    public Task DeleteAsync(string path, CancellationToken cancellationToken) =>
        throw new NotImplementedException("C-102");

    public Task DeleteFolderAsync(string folder, CancellationToken cancellationToken)
    {
        var fullPath = Resolve(folder);
        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }

        return Task.CompletedTask;
    }

    /// <summary>Turns a relative storage path into a full path, refusing anything that escapes the root ("../").</summary>
    private string Resolve(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var fullPath = Path.GetFullPath(Path.Combine(_root, relativePath));
        var rootWithSeparator = Path.TrimEndingDirectorySeparator(_root) + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Storage path '{relativePath}' points outside the storage folder.");
        }

        return fullPath;
    }
}
