using ObsidianWinSync.Configuration;
using ObsidianWinSync.Sync;

namespace ObsidianWinSync.Tests;

public sealed class BackupRestoreServiceTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly string _local;
    private readonly string _cloud;
    private readonly string _configPath;
    private readonly SyncConfiguration _configuration;

    public BackupRestoreServiceTests() {
        _local = Directory.CreateDirectory(Path.Combine(_root, "local")).FullName;
        _cloud = Directory.CreateDirectory(Path.Combine(_root, "cloud")).FullName;
        _configPath = Path.Combine(_root, "config.json");
        _configuration = new SyncConfiguration { LocalVaultPath = _local, IcloudVaultPath = _cloud };
    }

    [Fact]
    public async Task RestoreAsync_RestoresSelectedEntryThroughSyncLock() {
        string original = Path.Combine(_local, "folder", "note.md");
        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        await File.WriteAllTextAsync(original, "backup content");
        string statePath = _configuration.ResolveStatePath(_configPath);
        BackupManager manager = new();
        await manager.BackupAsync(
            new SyncAction("folder/note.md", SyncActionKind.DeleteLocal, null, null, null),
            _local,
            _cloud,
            statePath,
            new BackupConfiguration(),
            "run-restore");
        File.Delete(original);
        BackupEntry entry = Assert.Single(manager.List(statePath));
        BackupRestoreService service = new();

        Assert.False(service.DestinationExists(entry, _configuration));
        await service.RestoreAsync(entry, _configuration, _configPath, overwrite: false);

        Assert.Equal("backup content", await File.ReadAllTextAsync(original));
        Assert.True(service.DestinationExists(entry, _configuration));
    }

    [Fact]
    public async Task RestoreAsync_BacksUpCurrentFileBeforeOverwrite() {
        string path = Path.Combine(_local, "note.md");
        await File.WriteAllTextAsync(path, "old backup");
        string statePath = _configuration.ResolveStatePath(_configPath);
        BackupManager manager = new();
        await manager.BackupAsync(
            new SyncAction("note.md", SyncActionKind.DeleteLocal, null, null, null),
            _local,
            _cloud,
            statePath,
            new BackupConfiguration(),
            "original-run");
        BackupEntry original = Assert.Single(manager.List(statePath));
        await File.WriteAllTextAsync(path, "current before restore");

        await new BackupRestoreService().RestoreAsync(original, _configuration, _configPath, overwrite: true);

        Assert.Equal("old backup", await File.ReadAllTextAsync(path));
        IReadOnlyList<BackupEntry> entries = manager.List(statePath);
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, entry => entry.RunId.StartsWith("restore-", StringComparison.Ordinal));
    }

    [Fact]
    public void DestinationExists_RejectsPathOutsideVault() {
        BackupEntry entry = new("run", "local", "../outside.md", 1, DateTime.UtcNow);

        Assert.Throws<InvalidDataException>(() => new BackupRestoreService().DestinationExists(entry, _configuration));
    }

    [Fact]
    public void BackupListItem_FormatsIcloudSide() {
        DateTime created = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        BackupEntry entry = new("run", "icloud", "note.md", 123, created);

        BackupListItem item = BackupListItem.From(entry);

        Assert.Equal("iCloud", item.Side);
        Assert.Equal("note.md", item.RelativePath);
        Assert.Equal(123, item.Length);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
