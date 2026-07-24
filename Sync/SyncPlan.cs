namespace ObsidianWinSync.Sync;

public enum SyncActionKind {
    CopyLocalToCloud,
    CopyCloudToLocal,
    DeleteLocal,
    DeleteCloud,
    Conflict,
    AlreadySynchronized
}

public sealed record SyncAction(
    string RelativePath,
    SyncActionKind Kind,
    FileFingerprint? Local,
    FileFingerprint? Cloud,
    FileFingerprint? Baseline);

public sealed record SyncPlan(IReadOnlyList<SyncAction> Actions) {
    public bool HasConflicts => Actions.Any(action => action.Kind == SyncActionKind.Conflict);
}
