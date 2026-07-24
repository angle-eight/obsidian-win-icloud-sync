namespace ObsidianWinSync.Sync;

public sealed class ConcurrentSyncException : IOException {
    public ConcurrentSyncException(Exception innerException)
        : base("別の同期処理が実行中です。", innerException) {
    }
}
