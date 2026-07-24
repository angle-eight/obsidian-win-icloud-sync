using ObsidianWinSync.Sync;

namespace ObsidianWinSync.Tests;

public sealed class FileScannerTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public FileScannerTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ScanAsync_HashesFilesAndAppliesExclusions() {
        await File.WriteAllTextAsync(Path.Combine(_root, "日本語.md"), "content");
        await File.WriteAllTextAsync(Path.Combine(_root, "ignored.tmp"), "ignored");

        VaultSnapshot result = await new FileScanner().ScanAsync(_root, ["*.tmp"]);

        Assert.Contains("日本語.md", result.Files.Keys);
        Assert.DoesNotContain("ignored.tmp", result.Files.Keys);
        Assert.Equal(64, result.Files["日本語.md"].Hash.Length);
    }

    [Fact]
    public async Task ScanAsync_ReportsLockedFileInsteadOfOmittingIt() {
        string lockedPath = Path.Combine(_root, "locked.md");
        await File.WriteAllTextAsync(lockedPath, "content");
        await using FileStream fileLock = new(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        FileScanException error = await Assert.ThrowsAsync<FileScanException>(
            () => new FileScanner().ScanAsync(_root, []));

        Assert.Equal("locked.md", error.RelativePath);
        Assert.Equal("ファイル読み取り", error.Operation);
        Assert.IsAssignableFrom<IOException>(error.InnerException);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
