using ObsidianWinSync.Configuration;
using ObsidianWinSync.Sync;

namespace ObsidianWinSync.Tests;

public sealed class RetentionTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public RetentionTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task BackupManager_RemovesOldestRunWhenSizeLimitIsExceeded() {
        string local = Directory.CreateDirectory(Path.Combine(_root, "local")).FullName;
        string cloud = Directory.CreateDirectory(Path.Combine(_root, "cloud")).FullName;
        string statePath = Path.Combine(_root, "state", "state.json");
        string cloudFile = Path.Combine(cloud, "large.bin");
        await File.WriteAllBytesAsync(cloudFile, new byte[700 * 1024]);
        BackupManager manager = new();
        BackupConfiguration configuration = new() { RetentionDays = 30, MaximumSizeMb = 1 };
        SyncAction action = new("large.bin", SyncActionKind.CopyLocalToCloud, null, null, null);
        await manager.BackupAsync(action, local, cloud, statePath, configuration, "run-old");
        string oldRun = Path.Combine(Path.GetDirectoryName(statePath)!, "backup", "run-old");
        Directory.SetCreationTimeUtc(oldRun, DateTime.UtcNow.AddDays(-1));

        await manager.BackupAsync(action, local, cloud, statePath, configuration, "run-new");

        BackupEntry remaining = Assert.Single(manager.List(statePath));
        Assert.Equal("run-new", remaining.RunId);
    }

    [Fact]
    public async Task FileSyncLogger_RemovesExpiredLog() {
        string statePath = Path.Combine(_root, "logging", "state.json");
        string logDirectory = Path.Combine(Path.GetDirectoryName(statePath)!, "logs");
        Directory.CreateDirectory(logDirectory);
        string expired = Path.Combine(logDirectory, "2000-01-01.log");
        await File.WriteAllTextAsync(expired, "old");
        File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-10));

        await new FileSyncLogger(statePath, retentionDays: 2).WriteAsync("Information", "current");

        Assert.False(File.Exists(expired));
        Assert.True(File.Exists(Path.Combine(logDirectory, $"{DateTime.UtcNow:yyyy-MM-dd}.log")));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
