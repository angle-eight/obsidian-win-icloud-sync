using ObsidianWinSync.Sync;

namespace ObsidianWinSync.Tests;

public sealed class SyncIntegrationTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public SyncIntegrationTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Executor_CopiesFileInBothDirections() {
        string local = Directory.CreateDirectory(Path.Combine(_root, "local")).FullName;
        string cloud = Directory.CreateDirectory(Path.Combine(_root, "cloud")).FullName;
        await File.WriteAllTextAsync(Path.Combine(local, "note.md"), "hello");
        SyncAction action = new("note.md", SyncActionKind.CopyLocalToCloud, null, null, null);

        await new SyncExecutor().ExecuteAsync(action, local, cloud);

        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(cloud, "note.md")));
    }

    [Fact]
    public async Task StateStore_RoundTripsSnapshot() {
        string statePath = Path.Combine(_root, "state", "state.json");
        VaultSnapshot expected = new();
        expected.Files["folder/note.md"] = new FileFingerprint("ABC", 3, DateTime.UnixEpoch);
        SyncStateStore store = new();

        await store.SaveAsync(statePath, expected);
        VaultSnapshot actual = await store.LoadAsync(statePath);

        Assert.True(actual.Files["folder/note.md"].HasSameContent(expected.Files["folder/note.md"]));
        Assert.False(File.Exists(statePath + ".tmp"));
    }

    [Fact]
    public async Task StateStore_PreservesPreviousGeneration() {
        string statePath = Path.Combine(_root, "versioned", "state.json");
        SyncStateStore store = new();
        VaultSnapshot first = new();
        first.Files["old.md"] = new FileFingerprint("OLD", 3, DateTime.UnixEpoch);
        await store.SaveAsync(statePath, first);

        await store.SaveAsync(statePath, new VaultSnapshot());
        VaultSnapshot backup = await store.LoadAsync(statePath + ".bak");

        Assert.Contains("old.md", backup.Files.Keys);
    }

    [Fact]
    public async Task StateStore_MigratesVersionOneAndBindsConfiguredVault() {
        string statePath = Path.Combine(_root, "migration", "state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        string local = Directory.CreateDirectory(Path.Combine(_root, "migration-local")).FullName;
        string cloud = Directory.CreateDirectory(Path.Combine(_root, "migration-cloud")).FullName;
        await File.WriteAllTextAsync(statePath, """
            { "Version": 1, "CreatedAtUtc": "2024-01-01T00:00:00Z", "Files": {} }
            """);

        VaultSnapshot migrated = await new SyncStateStore().LoadAsync(statePath, local, cloud);

        Assert.Equal(2, migrated.Version);
        Assert.Equal(VaultIdentity.Create(local, cloud), migrated.Vault);
    }

    [Fact]
    public async Task StateStore_RejectsStateBelongingToAnotherVault() {
        string statePath = Path.Combine(_root, "mismatch", "state.json");
        string firstLocal = Directory.CreateDirectory(Path.Combine(_root, "first-local")).FullName;
        string firstCloud = Directory.CreateDirectory(Path.Combine(_root, "first-cloud")).FullName;
        string otherLocal = Directory.CreateDirectory(Path.Combine(_root, "other-local")).FullName;
        string otherCloud = Directory.CreateDirectory(Path.Combine(_root, "other-cloud")).FullName;
        SyncStateStore store = new();
        await store.SaveAsync(statePath, new VaultSnapshot { Vault = VaultIdentity.Create(firstLocal, firstCloud) });

        VaultMismatchException error = await Assert.ThrowsAsync<VaultMismatchException>(
            () => store.LoadAsync(statePath, otherLocal, otherCloud));

        Assert.Contains(firstLocal, error.Message);
        Assert.Contains(otherLocal, error.Message);
    }

    [Fact]
    public async Task StateStore_LoadsEarlierVersionTwoWithoutPendingConflicts() {
        string statePath = Path.Combine(_root, "v2-compatibility", "state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        string local = Directory.CreateDirectory(Path.Combine(_root, "v2-local")).FullName;
        string cloud = Directory.CreateDirectory(Path.Combine(_root, "v2-cloud")).FullName;
        string json = System.Text.Json.JsonSerializer.Serialize(new {
            Version = 2,
            CreatedAtUtc = DateTime.UtcNow,
            Vault = VaultIdentity.Create(local, cloud),
            Files = new Dictionary<string, FileFingerprint>()
        });
        await File.WriteAllTextAsync(statePath, json);

        VaultSnapshot state = await new SyncStateStore().LoadAsync(statePath, local, cloud);

        Assert.Empty(state.PendingConflicts);
    }

    [Fact]
    public async Task StateStore_ReportsWhenValidBackupCanRecoverCorruptState() {
        string statePath = Path.Combine(_root, "corrupt", "state.json");
        string local = Directory.CreateDirectory(Path.Combine(_root, "corrupt-local")).FullName;
        string cloud = Directory.CreateDirectory(Path.Combine(_root, "corrupt-cloud")).FullName;
        SyncStateStore store = new();
        VaultSnapshot state = new() { Vault = VaultIdentity.Create(local, cloud) };
        state.Files["note.md"] = new FileFingerprint("OLD", 3, DateTime.UnixEpoch);
        await store.SaveAsync(statePath, state);
        await store.SaveAsync(statePath, state);
        await File.WriteAllTextAsync(statePath, "{ broken json");

        StateCorruptionException error = await Assert.ThrowsAsync<StateCorruptionException>(
            () => store.LoadAsync(statePath, local, cloud));

        Assert.True(error.BackupAvailable);
        Assert.Equal(statePath + ".bak", error.BackupPath);
    }

    [Fact]
    public async Task StateStore_ReportsWhenBackupIsAlsoCorrupt() {
        string statePath = Path.Combine(_root, "double-corrupt", "state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        await File.WriteAllTextAsync(statePath, "not json");
        await File.WriteAllTextAsync(statePath + ".bak", "also not json");

        StateCorruptionException error = await Assert.ThrowsAsync<StateCorruptionException>(
            () => new SyncStateStore().LoadAsync(statePath));

        Assert.False(error.BackupAvailable);
        Assert.NotNull(error.BackupError);
    }

    [Fact]
    public async Task StateStore_RecoversBackupAndPreservesCorruptFile() {
        string statePath = Path.Combine(_root, "recover", "state.json");
        string local = Directory.CreateDirectory(Path.Combine(_root, "recover-local")).FullName;
        string cloud = Directory.CreateDirectory(Path.Combine(_root, "recover-cloud")).FullName;
        SyncStateStore store = new();
        VaultSnapshot state = new() { Vault = VaultIdentity.Create(local, cloud) };
        state.Files["note.md"] = new FileFingerprint("OLD", 3, DateTime.UnixEpoch);
        await store.SaveAsync(statePath, state);
        await store.SaveAsync(statePath, state);
        await File.WriteAllTextAsync(statePath, "corrupt contents");

        await store.RecoverFromBackupAsync(statePath, local, cloud);
        VaultSnapshot recovered = await store.LoadAsync(statePath, local, cloud);

        Assert.Contains("note.md", recovered.Files.Keys);
        string preserved = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(statePath)!, "state.json.corrupt-*"));
        Assert.Equal("corrupt contents", await File.ReadAllTextAsync(preserved));
        Assert.False(File.Exists(statePath + ".recovery.tmp"));
    }

    [Fact]
    public async Task BackupManager_CopiesDestinationBeforeOverwrite() {
        string local = Directory.CreateDirectory(Path.Combine(_root, "backup-local")).FullName;
        string cloud = Directory.CreateDirectory(Path.Combine(_root, "backup-cloud")).FullName;
        string statePath = Path.Combine(_root, "backup-state", "state.json");
        await File.WriteAllTextAsync(Path.Combine(cloud, "note.md"), "before");
        SyncAction action = new("note.md", SyncActionKind.CopyLocalToCloud, null, null, null);

        await new BackupManager().BackupAsync(
            action,
            local,
            cloud,
            statePath,
            new ObsidianWinSync.Configuration.BackupConfiguration(),
            "run-1");

        string backup = Path.Combine(_root, "backup-state", "backup", "run-1", "icloud", "note.md");
        Assert.Equal("before", await File.ReadAllTextAsync(backup));
    }

    [Fact]
    public async Task BackupManager_ListsAndRestoresBackup() {
        string local = Directory.CreateDirectory(Path.Combine(_root, "restore-local")).FullName;
        string cloud = Directory.CreateDirectory(Path.Combine(_root, "restore-cloud")).FullName;
        string statePath = Path.Combine(_root, "restore-state", "state.json");
        string original = Path.Combine(local, "folder", "note.md");
        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        await File.WriteAllTextAsync(original, "before");
        BackupManager manager = new();
        SyncAction action = new("folder/note.md", SyncActionKind.CopyCloudToLocal, null, null, null);
        await manager.BackupAsync(action, local, cloud, statePath, new ObsidianWinSync.Configuration.BackupConfiguration(), "run-2");
        File.Delete(original);

        BackupEntry entry = Assert.Single(manager.List(statePath));
        await manager.RestoreAsync(entry, local, cloud, statePath, overwrite: false);

        Assert.Equal("run-2", entry.RunId);
        Assert.Equal("local", entry.Side);
        Assert.Equal("before", await File.ReadAllTextAsync(original));
    }

    [Fact]
    public async Task BackupManager_DoesNotOverwriteWithoutPermission() {
        string local = Directory.CreateDirectory(Path.Combine(_root, "safe-local")).FullName;
        string cloud = Directory.CreateDirectory(Path.Combine(_root, "safe-cloud")).FullName;
        string statePath = Path.Combine(_root, "safe-state", "state.json");
        string original = Path.Combine(local, "note.md");
        await File.WriteAllTextAsync(original, "before");
        BackupManager manager = new();
        SyncAction action = new("note.md", SyncActionKind.DeleteLocal, null, null, null);
        await manager.BackupAsync(action, local, cloud, statePath, new ObsidianWinSync.Configuration.BackupConfiguration(), "run-3");
        await File.WriteAllTextAsync(original, "current");
        BackupEntry entry = Assert.Single(manager.List(statePath));

        await Assert.ThrowsAsync<IOException>(() => manager.RestoreAsync(entry, local, cloud, statePath, overwrite: false));

        Assert.Equal("current", await File.ReadAllTextAsync(original));
    }

    [Fact]
    public async Task Executor_PropagatesDeletion() {
        string local = Directory.CreateDirectory(Path.Combine(_root, "delete-local")).FullName;
        string cloud = Directory.CreateDirectory(Path.Combine(_root, "delete-cloud")).FullName;
        string cloudFile = Path.Combine(cloud, "note.md");
        await File.WriteAllTextAsync(cloudFile, "obsolete");
        SyncAction action = new("note.md", SyncActionKind.DeleteCloud, null, null, null);

        await new SyncExecutor().ExecuteAsync(action, local, cloud);

        Assert.False(File.Exists(cloudFile));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
