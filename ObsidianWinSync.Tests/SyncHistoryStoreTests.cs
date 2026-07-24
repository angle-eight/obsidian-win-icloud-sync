using ObsidianWinSync.Sync;

namespace ObsidianWinSync.Tests;

public sealed class SyncHistoryStoreTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly string _statePath;

    public SyncHistoryStoreTests() {
        Directory.CreateDirectory(_root);
        _statePath = Path.Combine(_root, "state.json");
    }

    [Fact]
    public async Task AppendAsync_StoresNewestFirstAndLimitsHistory() {
        SyncHistoryStore store = new();
        for (int index = 0; index < SyncHistoryStore.MaximumEntries + 3; index++) {
            DateTime started = DateTime.UnixEpoch.AddMinutes(index);
            await store.AppendAsync(_statePath, new SyncHistoryEntry(
                $"run-{index}", started, started.AddSeconds(2), SyncHistoryStatus.Success,
                false, index, 0, 0, null, null));
        }

        IReadOnlyList<SyncHistoryEntry> entries = await store.LoadAsync(_statePath);

        Assert.Equal(SyncHistoryStore.MaximumEntries, entries.Count);
        Assert.Equal($"run-{SyncHistoryStore.MaximumEntries + 2}", entries[0].RunId);
        Assert.Equal("run-3", entries[^1].RunId);
        Assert.False(File.Exists(store.GetPath(_statePath) + ".tmp"));
    }

    [Fact]
    public void HistoryListItem_FormatsFailureAndCounts() {
        DateTime started = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SyncHistoryEntry entry = new(
            "run", started, started.AddSeconds(1.5), SyncHistoryStatus.Failed,
            false, 2, 1, 3, "file_scan_io", "locked");

        SyncHistoryListItem item = SyncHistoryListItem.From(entry);

        Assert.Equal("失敗", item.Status);
        Assert.Equal("コピー 2 / 削除 1 / 競合 3", item.Counts);
        Assert.Equal("file_scan_io: locked", item.Error);
        Assert.Equal(1.5, item.Duration.TotalSeconds);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
