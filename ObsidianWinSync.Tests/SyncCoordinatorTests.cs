using ObsidianWinSync.Configuration;
using ObsidianWinSync.Sync;

namespace ObsidianWinSync.Tests;

public sealed class SyncCoordinatorTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public SyncCoordinatorTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task RunAsync_CopiesAndReportsResult() {
        string local = Directory.CreateDirectory(Path.Combine(_root, "local")).FullName;
        string cloud = Directory.CreateDirectory(Path.Combine(_root, "cloud")).FullName;
        string configPath = Path.Combine(_root, "config.json");
        await File.WriteAllTextAsync(Path.Combine(local, "note.md"), "hello");
        SyncConfiguration configuration = new() { LocalVaultPath = local, IcloudVaultPath = cloud, StateFilePath = "state/state.json" };

        SyncRunResult result = await new SyncCoordinator().RunAsync(configuration, configPath);

        Assert.Equal(1, result.CopiedCount);
        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(cloud, "note.md")));
        Assert.False(result.HasUnresolvedConflicts);
        VaultSnapshot state = await new SyncStateStore().LoadAsync(configuration.ResolveStatePath(configPath));
        Assert.Equal(VaultIdentity.Create(local, cloud), state.Vault);
        string log = await ReadLogAsync(configuration, configPath);
        Assert.Contains("event=sync_started", log);
        Assert.Contains("event=file_applied", log);
        Assert.Contains("event=sync_finished", log);
        SyncHistoryEntry history = Assert.Single(await new SyncHistoryStore().LoadAsync(configuration.ResolveStatePath(configPath)));
        Assert.Equal(SyncHistoryStatus.Success, history.Status);
        Assert.Equal(1, history.CopiedCount);
    }

    [Fact]
    public async Task RunAsync_DryRunDoesNotChangeFilesOrState() {
        string local = Directory.CreateDirectory(Path.Combine(_root, "dry-local")).FullName;
        string cloud = Directory.CreateDirectory(Path.Combine(_root, "dry-cloud")).FullName;
        string configPath = Path.Combine(_root, "dry-config.json");
        await File.WriteAllTextAsync(Path.Combine(local, "note.md"), "hello");
        SyncConfiguration configuration = new() { LocalVaultPath = local, IcloudVaultPath = cloud, StateFilePath = "dry-state/state.json" };

        SyncRunResult result = await new SyncCoordinator().RunAsync(configuration, configPath, dryRun: true);

        Assert.True(result.IsDryRun);
        Assert.False(File.Exists(Path.Combine(cloud, "note.md")));
        Assert.False(File.Exists(configuration.ResolveStatePath(configPath)));
    }

    [Fact]
    public async Task RunAsync_StopsBeforeCopyWhenStateBelongsToAnotherVault() {
        string local = Directory.CreateDirectory(Path.Combine(_root, "mismatch-local")).FullName;
        string cloud = Directory.CreateDirectory(Path.Combine(_root, "mismatch-cloud")).FullName;
        string otherLocal = Directory.CreateDirectory(Path.Combine(_root, "previous-local")).FullName;
        string otherCloud = Directory.CreateDirectory(Path.Combine(_root, "previous-cloud")).FullName;
        string configPath = Path.Combine(_root, "mismatch-config.json");
        SyncConfiguration configuration = new() { LocalVaultPath = local, IcloudVaultPath = cloud };
        await File.WriteAllTextAsync(Path.Combine(local, "must-not-copy.md"), "content");
        await new SyncStateStore().SaveAsync(
            configuration.ResolveStatePath(configPath),
            new VaultSnapshot { Vault = VaultIdentity.Create(otherLocal, otherCloud) });

        await Assert.ThrowsAsync<VaultMismatchException>(
            () => new SyncCoordinator().RunAsync(configuration, configPath));

        Assert.False(File.Exists(Path.Combine(cloud, "must-not-copy.md")));
    }

    [Fact]
    public async Task RunAsync_RecoversCorruptStateOnlyAfterApproval() {
        string local = Directory.CreateDirectory(Path.Combine(_root, "recovery-local")).FullName;
        string cloud = Directory.CreateDirectory(Path.Combine(_root, "recovery-cloud")).FullName;
        string configPath = Path.Combine(_root, "recovery-config.json");
        SyncConfiguration configuration = new() { LocalVaultPath = local, IcloudVaultPath = cloud };
        string statePath = configuration.ResolveStatePath(configPath);
        SyncStateStore store = new();
        VaultSnapshot state = new() { Vault = VaultIdentity.Create(local, cloud) };
        await store.SaveAsync(statePath, state);
        await store.SaveAsync(statePath, state);
        await File.WriteAllTextAsync(statePath, "broken");
        bool asked = false;

        await new SyncCoordinator().RunAsync(
            configuration,
            configPath,
            stateRecoveryResolver: (_, _) => {
                asked = true;
                return Task.FromResult(true);
            });

        Assert.True(asked);
        _ = await store.LoadAsync(statePath, local, cloud);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(statePath)!, "state.json.corrupt-*"));
    }

    [Fact]
    public async Task RunAsync_DoesNotRecoverCorruptStateWithoutApproval() {
        string local = Directory.CreateDirectory(Path.Combine(_root, "decline-local")).FullName;
        string cloud = Directory.CreateDirectory(Path.Combine(_root, "decline-cloud")).FullName;
        string configPath = Path.Combine(_root, "decline-config.json");
        SyncConfiguration configuration = new() { LocalVaultPath = local, IcloudVaultPath = cloud };
        string statePath = configuration.ResolveStatePath(configPath);
        SyncStateStore store = new();
        VaultSnapshot state = new() { Vault = VaultIdentity.Create(local, cloud) };
        await store.SaveAsync(statePath, state);
        await store.SaveAsync(statePath, state);
        await File.WriteAllTextAsync(statePath, "broken");

        await Assert.ThrowsAsync<StateCorruptionException>(() => new SyncCoordinator().RunAsync(
            configuration,
            configPath,
            stateRecoveryResolver: (_, _) => Task.FromResult(false)));

        Assert.Equal("broken", await File.ReadAllTextAsync(statePath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(statePath)!, "state.json.corrupt-*"));
    }

    [Fact]
    public async Task RunAsync_DoesNotTreatUnreadableCloudFileAsDeleted() {
        string local = Directory.CreateDirectory(Path.Combine(_root, "locked-local")).FullName;
        string cloud = Directory.CreateDirectory(Path.Combine(_root, "locked-cloud")).FullName;
        string configPath = Path.Combine(_root, "locked-config.json");
        SyncConfiguration configuration = new() { LocalVaultPath = local, IcloudVaultPath = cloud };
        string localFile = Path.Combine(local, "note.md");
        string cloudFile = Path.Combine(cloud, "note.md");
        await File.WriteAllTextAsync(localFile, "content");
        await new SyncCoordinator().RunAsync(configuration, configPath);
        string statePath = configuration.ResolveStatePath(configPath);
        byte[] stateBeforeFailure = await File.ReadAllBytesAsync(statePath);
        File.Delete(localFile);

        await using (FileStream fileLock = new(cloudFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
            FileScanException error = await Assert.ThrowsAsync<FileScanException>(
                () => new SyncCoordinator().RunAsync(configuration, configPath));
            Assert.Equal("note.md", error.RelativePath);
            Assert.True(File.Exists(cloudFile));
        }

        Assert.Equal(stateBeforeFailure, await File.ReadAllBytesAsync(statePath));
        Assert.Equal("content", await File.ReadAllTextAsync(cloudFile));
        string log = await ReadLogAsync(configuration, configPath);
        Assert.Contains("event=sync_failed", log);
        Assert.Contains("code=file_scan_io", log);
        Assert.Contains("transient=True", log);
        Assert.Contains("path=note.md", log);
        SyncHistoryEntry failed = (await new SyncHistoryStore().LoadAsync(statePath))[0];
        Assert.Equal(SyncHistoryStatus.Failed, failed.Status);
        Assert.Equal("file_scan_io", failed.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_LogsConflictAndCancellation() {
        string local = Directory.CreateDirectory(Path.Combine(_root, "cancel-local")).FullName;
        string cloud = Directory.CreateDirectory(Path.Combine(_root, "cancel-cloud")).FullName;
        string configPath = Path.Combine(_root, "cancel-config.json");
        SyncConfiguration configuration = new() { LocalVaultPath = local, IcloudVaultPath = cloud };
        await File.WriteAllTextAsync(Path.Combine(local, "conflict.md"), "local");
        await File.WriteAllTextAsync(Path.Combine(cloud, "conflict.md"), "cloud");

        await Assert.ThrowsAsync<OperationCanceledException>(() => new SyncCoordinator().RunAsync(
            configuration,
            configPath,
            conflictResolver: (_, _) => throw new OperationCanceledException()));

        string log = await ReadLogAsync(configuration, configPath);
        Assert.Contains("event=conflict_detected", log);
        Assert.Contains("path=conflict.md", log);
        Assert.Contains("event=sync_cancelled", log);
        Assert.Contains("code=cancelled", log);
        SyncHistoryEntry history = Assert.Single(await new SyncHistoryStore().LoadAsync(configuration.ResolveStatePath(configPath)));
        Assert.Equal(SyncHistoryStatus.Cancelled, history.Status);
    }

    [Fact]
    public async Task RunAsync_PersistsPendingConflictWhileSyncingOtherFiles() {
        string local = Directory.CreateDirectory(Path.Combine(_root, "pending-local")).FullName;
        string cloud = Directory.CreateDirectory(Path.Combine(_root, "pending-cloud")).FullName;
        string configPath = Path.Combine(_root, "pending-config.json");
        SyncConfiguration configuration = new() { LocalVaultPath = local, IcloudVaultPath = cloud };
        await File.WriteAllTextAsync(Path.Combine(local, "conflict.md"), "local version");
        await File.WriteAllTextAsync(Path.Combine(cloud, "conflict.md"), "cloud version");
        await File.WriteAllTextAsync(Path.Combine(local, "safe.md"), "safe content");

        SyncRunResult first = await new SyncCoordinator().RunAsync(configuration, configPath);
        VaultSnapshot firstState = await new SyncStateStore().LoadAsync(
            configuration.ResolveStatePath(configPath), local, cloud);

        Assert.Equal(1, first.ConflictCount);
        Assert.Equal("safe content", await File.ReadAllTextAsync(Path.Combine(cloud, "safe.md")));
        SyncHistoryEntry conflictHistory = Assert.Single(
            await new SyncHistoryStore().LoadAsync(configuration.ResolveStatePath(configPath)));
        Assert.Equal(SyncHistoryStatus.Conflicts, conflictHistory.Status);
        Assert.Equal(1, conflictHistory.ConflictCount);
        PendingConflict pending = Assert.Single(firstState.PendingConflicts).Value;
        Assert.Equal("conflict.md", pending.RelativePath);
        Assert.NotNull(pending.Local);
        Assert.NotNull(pending.Cloud);

        SyncRunResult second = await new SyncCoordinator().RunAsync(configuration, configPath);
        VaultSnapshot secondState = await new SyncStateStore().LoadAsync(
            configuration.ResolveStatePath(configPath), local, cloud);

        Assert.Equal(1, second.ConflictCount);
        Assert.Equal(pending.DetectedAtUtc, secondState.PendingConflicts["conflict.md"].DetectedAtUtc);
    }

    [Fact]
    public async Task RunAsync_RemovesPendingConflictAfterResolution() {
        string local = Directory.CreateDirectory(Path.Combine(_root, "resolve-local")).FullName;
        string cloud = Directory.CreateDirectory(Path.Combine(_root, "resolve-cloud")).FullName;
        string configPath = Path.Combine(_root, "resolve-config.json");
        SyncConfiguration configuration = new() { LocalVaultPath = local, IcloudVaultPath = cloud };
        await File.WriteAllTextAsync(Path.Combine(local, "conflict.md"), "local version");
        await File.WriteAllTextAsync(Path.Combine(cloud, "conflict.md"), "cloud version");
        await new SyncCoordinator().RunAsync(configuration, configPath);

        SyncRunResult resolved = await new SyncCoordinator().RunAsync(
            configuration,
            configPath,
            conflictResolver: (_, _) => Task.FromResult(ConflictChoice.Local));
        VaultSnapshot state = await new SyncStateStore().LoadAsync(
            configuration.ResolveStatePath(configPath), local, cloud);

        Assert.False(resolved.HasUnresolvedConflicts);
        Assert.Empty(state.PendingConflicts);
        Assert.Equal("local version", await File.ReadAllTextAsync(Path.Combine(cloud, "conflict.md")));
    }

    private static async Task<string> ReadLogAsync(SyncConfiguration configuration, string configPath) {
        string stateDirectory = Path.GetDirectoryName(configuration.ResolveStatePath(configPath))!;
        string logPath = Path.Combine(stateDirectory, "logs", $"{DateTime.UtcNow:yyyy-MM-dd}.log");
        return await File.ReadAllTextAsync(logPath);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
