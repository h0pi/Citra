using System.Text;
using Citra.Application.Common.Storage;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Citra.IntegrationTests.Storage;

/// <summary>The real LocalFileStorage (as registered in DI), writing to the test run's private folder.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class LocalFileStorageTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IFileStorage Storage => fixture.OpenScope(tenantId: null).Services.GetRequiredService<IFileStorage>();

    /// <summary>A unique document folder per test, laid out exactly like production paths.</summary>
    private static string NewFilePath(string fileName = "report.pdf") =>
        StoragePaths.OriginalFile(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), fileName);

    private string OnDisk(string path) => Path.Combine(fixture.StorageRoot, path);

    [Fact]
    public async Task Saves_a_file_and_reads_back_the_same_bytes()
    {
        var path = NewFilePath();
        var bytes = RandomBytes(5 * 1024 * 1024 + 123); // bigger than the copy buffer

        var written = await Storage.SaveAsync(path, new MemoryStream(bytes), Ct);

        written.ShouldBe(bytes.Length);
        File.Exists(OnDisk(path)).ShouldBeTrue("folders are created as needed");
        (await ReadAllAsync(path)).ShouldBe(bytes);
    }

    [Fact]
    public async Task Saving_again_replaces_the_file()
    {
        var path = NewFilePath();
        await Storage.SaveAsync(path, Text("first version, longer"), Ct);

        await Storage.SaveAsync(path, Text("second"), Ct);

        Encoding.UTF8.GetString(await ReadAllAsync(path)).ShouldBe("second");
    }

    [Fact]
    public async Task A_cancelled_upload_leaves_no_file_behind()
    {
        var path = NewFilePath();
        using var cancel = new CancellationTokenSource();
        var upload = new CallbackStream(RandomBytes(1024 * 1024), onFirstRead: cancel.Cancel);

        await Should.ThrowAsync<OperationCanceledException>(() => Storage.SaveAsync(path, upload, cancel.Token));

        File.Exists(OnDisk(path)).ShouldBeFalse();
        TempFilesNextTo(path).ShouldBeEmpty("the temporary file must be cleaned up");
    }

    [Fact]
    public async Task A_failed_upload_keeps_the_previous_version()
    {
        var path = NewFilePath();
        await Storage.SaveAsync(path, Text("good version"), Ct);

        var brokenUpload = new CallbackStream(
            RandomBytes(1024 * 1024), onFirstRead: () => throw new IOException("connection reset"));
        await Should.ThrowAsync<IOException>(() => Storage.SaveAsync(path, brokenUpload, Ct));

        Encoding.UTF8.GetString(await ReadAllAsync(path)).ShouldBe("good version");
        TempFilesNextTo(path).ShouldBeEmpty();
    }

    [Fact]
    public async Task Opening_a_missing_file_returns_null()
    {
        (await Storage.OpenReadAsync(NewFilePath(), Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_file_can_be_deleted_while_it_is_being_read()
    {
        var path = NewFilePath();
        await Storage.SaveAsync(path, Text("still downloading"), Ct);

        await using var reader = (await Storage.OpenReadAsync(path, Ct))!;
        await Storage.DeleteAsync(path, Ct);

        File.Exists(OnDisk(path)).ShouldBeFalse();
    }

    [Fact]
    public async Task Deletes_a_file_and_ignores_files_that_are_already_gone()
    {
        var path = NewFilePath();
        await Storage.SaveAsync(path, Text("bye"), Ct);

        await Storage.DeleteAsync(path, Ct);
        await Storage.DeleteAsync(path, Ct);
        await Storage.DeleteAsync(NewFilePath(), Ct); // folder does not even exist

        (await Storage.OpenReadAsync(path, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Deleting_a_document_folder_removes_only_that_document()
    {
        var (tenant, bot) = (Guid.NewGuid(), Guid.NewGuid());
        var (docA, docB) = (Guid.NewGuid(), Guid.NewGuid());
        var fileA = StoragePaths.OriginalFile(tenant, bot, docA, "a.pdf");
        var fileB = StoragePaths.OriginalFile(tenant, bot, docB, "b.pdf");
        await Storage.SaveAsync(fileA, Text("a"), Ct);
        await Storage.SaveAsync(fileB, Text("b"), Ct);

        await Storage.DeleteFolderAsync(StoragePaths.DocumentFolder(tenant, bot, docA), Ct);

        (await Storage.OpenReadAsync(fileA, Ct)).ShouldBeNull();
        (await ReadAllAsync(fileB)).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("a/../../outside.txt")]
    [InlineData("..\\outside.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    public async Task Refuses_paths_outside_the_storage_folder(string path)
    {
        await Should.ThrowAsync<InvalidOperationException>(() => Storage.SaveAsync(path, Text("x"), Ct));
        await Should.ThrowAsync<InvalidOperationException>(() => Storage.OpenReadAsync(path, Ct));
        await Should.ThrowAsync<InvalidOperationException>(() => Storage.DeleteAsync(path, Ct));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("./")]
    [InlineData(".\\")]
    [InlineData("a/..")]
    [InlineData("a/../")]
    [InlineData("a/./..//")]
    public async Task Never_deletes_the_whole_storage_folder(string folder)
    {
        var survivor = NewFilePath();
        await Storage.SaveAsync(survivor, Text("other tenants' data"), Ct);

        await Should.ThrowAsync<InvalidOperationException>(() => Storage.DeleteFolderAsync(folder, Ct));

        (await ReadAllAsync(survivor)).ShouldNotBeEmpty();
    }

    // ---------- helpers ----------

    private async Task<byte[]> ReadAllAsync(string path)
    {
        await using var stream = await Storage.OpenReadAsync(path, Ct) ?? throw new FileNotFoundException(path);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, Ct);
        return copy.ToArray();
    }

    private string[] TempFilesNextTo(string path)
    {
        var folder = Path.GetDirectoryName(OnDisk(path))!;
        return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.tmp") : [];
    }

    private static MemoryStream Text(string text) => new(Encoding.UTF8.GetBytes(text));

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }

    /// <summary>An upload stream that runs a callback (cancel, or fail) when the copy starts reading.</summary>
    private sealed class CallbackStream(byte[] data, Action onFirstRead) : MemoryStream(data)
    {
        private bool _started;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_started)
            {
                _started = true;
                onFirstRead();
            }

            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
