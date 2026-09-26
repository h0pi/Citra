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
    private const int BufferSize = 81_920;

    // Windows and macOS file systems ignore case; Linux does not, so "../Storage" is a different folder there.
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    // Relative roots resolve from the current directory, which is the project folder under dotnet run and VS.
    private readonly string _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Value.LocalRootPath));

    public async Task<long> SaveAsync(string path, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var fullPath = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        // Write next to the target under a temporary name, then rename: readers never see a half-written file,
        // and a failed or cancelled upload leaves the previous version (if any) untouched.
        var tempPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            long length;
            await using (var file = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                await content.CopyToAsync(file, BufferSize, cancellationToken);
                await file.FlushAsync(cancellationToken);
                length = file.Length;
            }

            File.Move(tempPath, fullPath, overwrite: true);
            return length;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    public Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        var fullPath = Resolve(path);
        try
        {
            // FileShare.Delete: a document may be deleted while someone is still downloading it.
            Stream stream = new FileStream(
                fullPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, BufferSize, useAsync: true);
            return Task.FromResult<Stream?>(stream);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        var fullPath = Resolve(path);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public Task DeleteFolderAsync(string folder, CancellationToken cancellationToken)
    {
        var fullPath = Resolve(folder);
        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Turns a relative storage path into a full path strictly inside the root. Refuses "../" escapes,
    /// absolute paths and the root itself (so a bad folder path can never delete all tenants' files).
    /// </summary>
    private string Resolve(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        // Trim trailing separators first: "./" must resolve to the root itself (and be refused),
        // not to "root/", which would pass the check below and let DeleteFolderAsync wipe everything.
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(_root, relativePath)));
        if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, PathComparison))
        {
            throw new InvalidOperationException($"Storage path '{relativePath}' points outside the storage folder.");
        }

        return fullPath;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort: a leftover *.tmp file is harmless and never read.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
