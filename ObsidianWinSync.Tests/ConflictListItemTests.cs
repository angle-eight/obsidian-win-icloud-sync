using ObsidianWinSync.Sync;

namespace ObsidianWinSync.Tests;

public sealed class ConflictListItemTests {
    [Theory]
    [InlineData(true, true, "両側で変更")]
    [InlineData(false, true, "ローカルで削除")]
    [InlineData(true, false, "iCloudで削除")]
    public void From_DescribesConflictAndPreservesMetadata(bool hasLocal, bool hasCloud, string expectedKind) {
        DateTime detected = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        FileFingerprint fingerprint = new("HASH", 123, detected.AddMinutes(-1));
        PendingConflict pending = new(
            "folder/note.md",
            detected,
            hasLocal ? fingerprint : null,
            hasCloud ? fingerprint : null,
            null);

        ConflictListItem item = ConflictListItem.From(pending);

        Assert.Equal("folder/note.md", item.RelativePath);
        Assert.Equal(expectedKind, item.Kind);
        Assert.Equal(detected, item.DetectedAtUtc);
        Assert.Equal(hasLocal ? (long?)123 : null, item.LocalLength);
        Assert.Equal(hasCloud ? (long?)123 : null, item.IcloudLength);
    }
}
