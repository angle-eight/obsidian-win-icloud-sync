namespace ObsidianWinSync.Sync;

public sealed record PendingConflict(
    string RelativePath,
    DateTime DetectedAtUtc,
    FileFingerprint? Local,
    FileFingerprint? Cloud,
    FileFingerprint? Baseline) {
    public bool HasSameVersions(SyncAction action) =>
        Same(Local, action.Local) && Same(Cloud, action.Cloud) && Same(Baseline, action.Baseline);

    private static bool Same(FileFingerprint? left, FileFingerprint? right) =>
        left is null ? right is null : left.HasSameContent(right);
}
