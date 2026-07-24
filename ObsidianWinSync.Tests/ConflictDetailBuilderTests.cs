using ObsidianWinSync.Sync;

namespace ObsidianWinSync.Tests;

public sealed class ConflictDetailBuilderTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly string _local;
    private readonly string _cloud;

    public ConflictDetailBuilderTests() {
        _local = Directory.CreateDirectory(Path.Combine(_root, "local")).FullName;
        _cloud = Directory.CreateDirectory(Path.Combine(_root, "cloud")).FullName;
    }

    [Fact]
    public async Task Build_ShowsUtf8LineDiffAndNormalizesLineEndings() {
        string relativePath = "folder/日本語.md";
        Directory.CreateDirectory(Path.Combine(_local, "folder"));
        Directory.CreateDirectory(Path.Combine(_cloud, "folder"));
        await File.WriteAllTextAsync(Path.Combine(_local, relativePath), "共通\r\nローカル\r\n末尾");
        await File.WriteAllTextAsync(Path.Combine(_cloud, relativePath), "共通\nクラウド\n追加\n末尾");
        PendingConflict conflict = CreateConflict(relativePath, 24);

        ConflictDetail detail = ConflictDetailBuilder.Build(conflict, _local, _cloud);

        Assert.True(detail.IsTextDiff);
        Assert.Contains("共通", detail.Text);
        Assert.Contains("-         2", detail.Text);
        Assert.Contains("ローカル", detail.Text);
        Assert.Contains("+", detail.Text);
        Assert.Contains("クラウド", detail.Text);
        Assert.Contains("追加", detail.Text);
    }

    [Fact]
    public void Build_ShowsBinaryMetadataIncludingHashSizeAndDeletion() {
        PendingConflict conflict = new(
            "image.png",
            DateTime.UtcNow,
            new FileFingerprint("LOCAL_HASH", 456, DateTime.UnixEpoch),
            null,
            null);

        ConflictDetail detail = ConflictDetailBuilder.Build(conflict, _local, _cloud);

        Assert.False(detail.IsTextDiff);
        Assert.Contains("456 bytes", detail.Text);
        Assert.Contains("LOCAL_HASH", detail.Text);
        Assert.Contains("[iCloud]", detail.Text);
        Assert.Contains("削除済み", detail.Text);
    }

    [Fact]
    public async Task Build_TreatsInvalidUtf8WithTextExtensionAsBinary() {
        string path = "binary.md";
        await File.WriteAllBytesAsync(Path.Combine(_local, path), [0xFF, 0xFE, 0x00]);
        await File.WriteAllBytesAsync(Path.Combine(_cloud, path), [0xFF, 0xFE, 0x01]);
        PendingConflict conflict = CreateConflict(path, 3);

        ConflictDetail detail = ConflictDetailBuilder.Build(conflict, _local, _cloud);

        Assert.False(detail.IsTextDiff);
        Assert.Contains("SHA-256", detail.Text);
    }

    private static PendingConflict CreateConflict(string path, long length) {
        DateTime timestamp = DateTime.UtcNow;
        return new PendingConflict(
            path,
            timestamp,
            new FileFingerprint("LOCAL", length, timestamp),
            new FileFingerprint("CLOUD", length, timestamp),
            null);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
