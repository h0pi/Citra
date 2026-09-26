using Citra.Application.Common.Storage;

namespace Citra.IntegrationTests.Storage;

/// <summary>The storage layout is a contract: files already on disk depend on it never changing silently.</summary>
public sealed class StoragePathsTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bot = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Doc = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void Files_live_in_tenant_bot_document_folders()
    {
        StoragePaths.BotFolder(Tenant, Bot).ShouldBe($"{Tenant}/{Bot}");
        StoragePaths.DocumentFolder(Tenant, Bot, Doc).ShouldBe($"{Tenant}/{Bot}/{Doc}");
        StoragePaths.OriginalFile(Tenant, Bot, Doc, "Employee Handbook.PDF")
            .ShouldBe($"{Tenant}/{Bot}/{Doc}/original.pdf");
    }

    [Fact]
    public void A_document_folder_is_inside_its_bot_folder()
    {
        StoragePaths.OriginalFile(Tenant, Bot, Doc, "a.pdf")
            .ShouldStartWith(StoragePaths.DocumentFolder(Tenant, Bot, Doc) + "/");
        StoragePaths.DocumentFolder(Tenant, Bot, Doc).ShouldStartWith(StoragePaths.BotFolder(Tenant, Bot) + "/");
    }

    [Theory]
    [InlineData("report.pdf", ".pdf")]
    [InlineData("REPORT.DOCX", ".docx")]
    [InlineData("notes.md", ".md")]
    [InlineData("archive.tar.gz", ".gz")]
    [InlineData("no-extension", "")]
    [InlineData("trailing-dot.", "")]
    [InlineData("", "")]
    [InlineData("../../etc/passwd", "")]
    [InlineData("evil.p/../df", "")]
    [InlineData("weird.p df", "")]
    [InlineData("unicode.pdf\u202Eexe", "")]
    [InlineData("long.averyveryverylongextension", "")]
    public void Only_a_clean_short_extension_is_kept(string fileName, string expected) =>
        StoragePaths.SafeExtension(fileName).ShouldBe(expected);

    [Theory]
    [InlineData("../../../outside.pdf")]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData("/etc/passwd")]
    public void The_user_file_name_never_becomes_part_of_the_path(string fileName)
    {
        var path = StoragePaths.OriginalFile(Tenant, Bot, Doc, fileName);

        path.ShouldStartWith($"{Tenant}/{Bot}/{Doc}/original");
        path.ShouldNotContain("..");
        path.ShouldNotContain("\\");
        path.ShouldNotContain(":");
    }
}
