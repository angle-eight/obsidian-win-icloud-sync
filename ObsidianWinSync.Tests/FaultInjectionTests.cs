using ObsidianWinSync.Configuration;
using ObsidianWinSync.Sync;

namespace ObsidianWinSync.Tests;

public sealed class FaultInjectionTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly string _local;
    private readonly string _cloud;

    public FaultInjectionTests() {
        _local = Directory.CreateDirectory(Path.Combine(_root, "local")).FullName;
        _cloud = Directory.CreateDirectory(Path.Combine(_root, "cloud")).FullName;
    }

    [Fact]
    public async Task Executor_IoFailureBeforeReplacePreservesDestinationAndCleansTemporaryFile() {
        string source = Path.Combine(_local, "note.md");
        string destination = Path.Combine(_cloud, "note.md");
        await File.WriteAllTextAsync(source, "new content");
        await File.WriteAllTextAsync(destination, "existing content");
        SyncExecutor executor = new((stage, _, _) => stage == SyncExecutionStage.BeforeReplace
            ? Task.FromException(new IOException("simulated disk full"))
            : Task.CompletedTask);

        await Assert.ThrowsAsync<IOException>(() => executor.ExecuteAsync(
            new SyncAction("note.md", SyncActionKind.CopyLocalToCloud, null, null, null), _local, _cloud));

        Assert.Equal("existing content", await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.GetFiles(_cloud, "*.obsidianwinsync.*.tmp"));
    }

    [Fact]
    public async Task Executor_CancellationAfterPartialCopyCleansTemporaryFile() {
        string source = Path.Combine(_local, "cancel.bin");
        await File.WriteAllBytesAsync(source, new byte[2 * 1024 * 1024]);
        SyncExecutor executor = new((stage, _, _) => stage == SyncExecutionStage.AfterTemporaryCopy
            ? Task.FromException(new OperationCanceledException("injected cancellation"))
            : Task.CompletedTask);

        await Assert.ThrowsAsync<OperationCanceledException>(() => executor.ExecuteAsync(
            new SyncAction("cancel.bin", SyncActionKind.CopyLocalToCloud, null, null, null), _local, _cloud));

        Assert.False(File.Exists(Path.Combine(_cloud, "cancel.bin")));
        Assert.Empty(Directory.GetFiles(_cloud, "*.obsidianwinsync.*.tmp"));
    }

    [Fact]
    public async Task Executor_FailureBeforeDeletePreservesFile() {
        string destination = Path.Combine(_cloud, "keep.md");
        await File.WriteAllTextAsync(destination, "keep");
        SyncExecutor executor = new((stage, _, _) => stage == SyncExecutionStage.BeforeDelete
            ? Task.FromException(new IOException("injected delete failure"))
            : Task.CompletedTask);

        await Assert.ThrowsAsync<IOException>(() => executor.ExecuteAsync(
            new SyncAction("keep.md", SyncActionKind.DeleteCloud, null, null, null), _local, _cloud));

        Assert.Equal("keep", await File.ReadAllTextAsync(destination));
    }

    [Fact]
    public async Task StateStore_CancellationPreservesExistingStateAndCleansTemporaryFile() {
        string statePath = Path.Combine(_root, "state", "state.json");
        SyncStateStore store = new();
        VaultSnapshot existing = new();
        existing.Files["existing.md"] = new FileFingerprint("OLD", 3, DateTime.UnixEpoch);
        await store.SaveAsync(statePath, existing);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SaveAsync(statePath, new VaultSnapshot(), cancellation.Token));

        Assert.Contains("existing.md", (await store.LoadAsync(statePath)).Files.Keys);
        Assert.False(File.Exists(statePath + ".tmp"));
    }

    [Fact]
    public async Task BackupCancellationDoesNotPublishPartialBackup() {
        string cloudFile = Path.Combine(_cloud, "backup.bin");
        await File.WriteAllBytesAsync(cloudFile, new byte[2 * 1024 * 1024]);
        string statePath = Path.Combine(_root, "backup-state", "state.json");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new BackupManager().BackupAsync(
            new SyncAction("backup.bin", SyncActionKind.DeleteCloud, null, null, null),
            _local,
            _cloud,
            statePath,
            new BackupConfiguration(),
            "cancelled-run",
            cancellation.Token));

        Assert.Empty(new BackupManager().List(statePath));
        string backupRoot = Path.Combine(Path.GetDirectoryName(statePath)!, "backup");
        Assert.Empty(Directory.GetFiles(backupRoot, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Coordinator_RecordsInjectedDiskFailureWithoutPublishingPartialFileOrState() {
        await File.WriteAllTextAsync(Path.Combine(_local, "disk.md"), "content");
        string configPath = Path.Combine(_root, "disk-config.json");
        SyncConfiguration configuration = new() { LocalVaultPath = _local, IcloudVaultPath = _cloud };
        SyncExecutor executor = new((stage, _, _) => stage == SyncExecutionStage.BeforeReplace
            ? Task.FromException(new IOException("simulated disk full"))
            : Task.CompletedTask);

        await Assert.ThrowsAsync<IOException>(
            () => new SyncCoordinator(executor: executor).RunAsync(configuration, configPath));

        Assert.False(File.Exists(Path.Combine(_cloud, "disk.md")));
        Assert.False(File.Exists(configuration.ResolveStatePath(configPath)));
        SyncHistoryEntry failure = Assert.Single(
            await new SyncHistoryStore().LoadAsync(configuration.ResolveStatePath(configPath)));
        Assert.Equal(SyncHistoryStatus.Failed, failure.Status);
        Assert.Equal("io", failure.ErrorCode);
    }

    [Fact]
    public async Task Coordinator_SynchronizesUnicodeEmojiLongPathAndLargeFile() {
        string relativeDirectory = Path.Combine(
            "日本語ノート", "階層01", "階層02", "階層03", "階層04", "emoji-📚");
        string localDirectory = Directory.CreateDirectory(Path.Combine(_local, relativeDirectory)).FullName;
        string fileName = "長い名前のノート-😀-同期テスト.bin";
        string localPath = Path.Combine(localDirectory, fileName);
        byte[] content = new byte[2 * 1024 * 1024];
        new Random(1234).NextBytes(content);
        await File.WriteAllBytesAsync(localPath, content);
        string configPath = Path.Combine(_root, "unicode-config.json");
        SyncConfiguration configuration = new() { LocalVaultPath = _local, IcloudVaultPath = _cloud };

        SyncRunResult result = await new SyncCoordinator().RunAsync(configuration, configPath);

        string cloudPath = Path.Combine(_cloud, relativeDirectory, fileName);
        Assert.Equal(1, result.CopiedCount);
        Assert.Equal(content, await File.ReadAllBytesAsync(cloudPath));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
