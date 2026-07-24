namespace ObsidianWinSync.Sync;

public sealed record BackupListItem(
    DateTime CreatedAtLocal,
    string RunId,
    string Side,
    string RelativePath,
    long Length) {
    public static BackupListItem From(BackupEntry entry) => new(
        entry.CreatedAtUtc.ToLocalTime(),
        entry.RunId,
        entry.Side == "local" ? "local" : "iCloud",
        entry.RelativePath,
        entry.Length);
}
