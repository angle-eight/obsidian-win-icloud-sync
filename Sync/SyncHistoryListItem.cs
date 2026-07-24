namespace ObsidianWinSync.Sync;

public sealed record SyncHistoryListItem(
    DateTime StartedAtLocal,
    string Status,
    string Counts,
    TimeSpan Duration,
    string Error) {
    public static SyncHistoryListItem From(SyncHistoryEntry entry) => new(
        entry.StartedAtUtc.ToLocalTime(),
        entry.Status switch {
            SyncHistoryStatus.Success => entry.IsDryRun ? "dry-run成功" : "成功",
            SyncHistoryStatus.Conflicts => "競合あり",
            SyncHistoryStatus.Cancelled => "キャンセル",
            _ => "失敗"
        },
        $"コピー {entry.CopiedCount} / 削除 {entry.DeletedCount} / 競合 {entry.ConflictCount}",
        entry.FinishedAtUtc - entry.StartedAtUtc,
        entry.ErrorCode is null ? "" : $"{entry.ErrorCode}: {entry.ErrorMessage}");
}
