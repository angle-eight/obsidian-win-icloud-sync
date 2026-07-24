namespace ObsidianWinSync.Sync;

public sealed record SyncFailureClassification(string Code, bool IsTransient);

public static class SyncFailureClassifier {
    public static SyncFailureClassification Classify(Exception exception) => exception switch {
        OperationCanceledException => new("cancelled", false),
        StateCorruptionException => new("state_corruption", false),
        VaultMismatchException => new("vault_mismatch", false),
        ConcurrentSyncException => new("concurrent_sync", true),
        FileScanException { InnerException: UnauthorizedAccessException } => new("file_scan_access_denied", false),
        FileScanException => new("file_scan_io", true),
        UnauthorizedAccessException => new("access_denied", false),
        InvalidDataException => new("invalid_data", false),
        IOException => new("io", true),
        _ => new("unexpected", false)
    };
}
