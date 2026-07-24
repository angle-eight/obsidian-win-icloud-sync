namespace ObsidianWinSync.Sync;

public enum ConflictChoice {
    Skip,
    Local,
    Icloud
}

public sealed record SyncFileResult(string RelativePath, SyncActionKind PlannedAction, SyncActionKind? AppliedAction);

public sealed record SyncRunResult(DateTime StartedAtUtc, DateTime FinishedAtUtc, bool IsDryRun, IReadOnlyList<SyncFileResult> Files) {
    public int CopiedCount => Files.Count(file => file.AppliedAction is SyncActionKind.CopyLocalToCloud or SyncActionKind.CopyCloudToLocal);
    public int DeletedCount => Files.Count(file => file.AppliedAction is SyncActionKind.DeleteLocal or SyncActionKind.DeleteCloud);
    public int ConflictCount => Files.Count(file => file.PlannedAction == SyncActionKind.Conflict && file.AppliedAction is null);
    public bool HasUnresolvedConflicts => ConflictCount > 0;
}
