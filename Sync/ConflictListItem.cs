namespace ObsidianWinSync.Sync;

public sealed record ConflictListItem(
    string RelativePath,
    string Kind,
    DateTime DetectedAtUtc,
    DateTime? LocalModifiedAtUtc,
    long? LocalLength,
    DateTime? IcloudModifiedAtUtc,
    long? IcloudLength) {
    public static ConflictListItem From(PendingConflict conflict) => new(
        conflict.RelativePath,
        conflict.Local is null ? "ローカルで削除" : conflict.Cloud is null ? "iCloudで削除" : "両側で変更",
        conflict.DetectedAtUtc,
        conflict.Local?.LastWriteTimeUtc,
        conflict.Local?.Length,
        conflict.Cloud?.LastWriteTimeUtc,
        conflict.Cloud?.Length);
}
