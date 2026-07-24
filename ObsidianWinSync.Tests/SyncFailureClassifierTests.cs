using ObsidianWinSync.Sync;

namespace ObsidianWinSync.Tests;

public sealed class SyncFailureClassifierTests {
    [Fact]
    public void Classify_DistinguishesTransientAndPermanentFailures() {
        SyncFailureClassification scan = SyncFailureClassifier.Classify(
            new FileScanException("C:\\vault", "note.md", "ファイル読み取り", new IOException("locked")));
        SyncFailureClassification access = SyncFailureClassifier.Classify(new UnauthorizedAccessException());
        SyncFailureClassification state = SyncFailureClassifier.Classify(
            new StateCorruptionException("state.json", false, null, new InvalidDataException()));
        SyncFailureClassification concurrent = SyncFailureClassifier.Classify(new ConcurrentSyncException(new IOException()));

        Assert.Equal(new SyncFailureClassification("file_scan_io", true), scan);
        Assert.Equal(new SyncFailureClassification("access_denied", false), access);
        Assert.Equal(new SyncFailureClassification("state_corruption", false), state);
        Assert.Equal(new SyncFailureClassification("concurrent_sync", true), concurrent);
    }
}
