using ObsidianWinSync.Configuration;
using ObsidianWinSync.Sync;

namespace ObsidianWinSync.Tests;

public sealed class ConflictResolutionServiceTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly string _local;
    private readonly string _cloud;
    private readonly string _configPath;
    private readonly SyncConfiguration _configuration;

    public ConflictResolutionServiceTests() {
        _local = Directory.CreateDirectory(Path.Combine(_root, "local")).FullName;
        _cloud = Directory.CreateDirectory(Path.Combine(_root, "cloud")).FullName;
        _configPath = Path.Combine(_root, "config.json");
        _configuration = new SyncConfiguration { LocalVaultPath = _local, IcloudVaultPath = _cloud };
    }

    [Fact]
    public async Task ResolveAsync_AppliesSelectedSideInBatchAndCreatesBackups() {
        await CreateInitialConflictAsync("first.md", "local first", "cloud first");
        await CreateInitialConflictAsync("second.md", "local second", "cloud second");
        await new SyncCoordinator().RunAsync(_configuration, _configPath);
        VaultSnapshot state = await LoadStateAsync();

        ConflictResolutionResult result = await new ConflictResolutionService().ResolveAsync(
            _configuration,
            _configPath,
            state.PendingConflicts.Values.ToArray(),
            ConflictChoice.Local);

        Assert.Equal(2, result.AppliedCount);
        Assert.False(result.RequiresReview);
        Assert.Equal("local first", await File.ReadAllTextAsync(Path.Combine(_cloud, "first.md")));
        Assert.Equal("local second", await File.ReadAllTextAsync(Path.Combine(_cloud, "second.md")));
        Assert.Empty((await LoadStateAsync()).PendingConflicts);
        Assert.Equal(2, new BackupManager().List(_configuration.ResolveStatePath(_configPath)).Count);
    }

    [Fact]
    public async Task ResolveAsync_StopsWholeBatchAndRefreshesPendingStateWhenAFileChanged() {
        await CreateInitialConflictAsync("stable.md", "stable local", "stable cloud");
        await CreateInitialConflictAsync("changed.md", "old local", "cloud value");
        await new SyncCoordinator().RunAsync(_configuration, _configPath);
        VaultSnapshot before = await LoadStateAsync();
        await File.WriteAllTextAsync(Path.Combine(_local, "changed.md"), "new local");

        ConflictResolutionResult result = await new ConflictResolutionService().ResolveAsync(
            _configuration,
            _configPath,
            before.PendingConflicts.Values.ToArray(),
            ConflictChoice.Local);

        Assert.Equal(0, result.AppliedCount);
        Assert.Equal(["changed.md"], result.ChangedPaths);
        Assert.Equal("stable cloud", await File.ReadAllTextAsync(Path.Combine(_cloud, "stable.md")));
        Assert.Equal("cloud value", await File.ReadAllTextAsync(Path.Combine(_cloud, "changed.md")));
        VaultSnapshot refreshed = await LoadStateAsync();
        Assert.NotEqual(
            before.PendingConflicts["changed.md"].Local!.Hash,
            refreshed.PendingConflicts["changed.md"].Local!.Hash);

        ConflictResolutionResult confirmed = await new ConflictResolutionService().ResolveAsync(
            _configuration,
            _configPath,
            refreshed.PendingConflicts.Values.ToArray(),
            ConflictChoice.Local);

        Assert.Equal(2, confirmed.AppliedCount);
        Assert.Equal("new local", await File.ReadAllTextAsync(Path.Combine(_cloud, "changed.md")));
    }

    [Fact]
    public async Task ResolveAsync_PropagatesSelectedDeletion() {
        string localPath = Path.Combine(_local, "deleted.md");
        string cloudPath = Path.Combine(_cloud, "deleted.md");
        await File.WriteAllTextAsync(localPath, "baseline");
        await new SyncCoordinator().RunAsync(_configuration, _configPath);
        File.Delete(localPath);
        await File.WriteAllTextAsync(cloudPath, "cloud changed");
        await new SyncCoordinator().RunAsync(_configuration, _configPath);
        PendingConflict pending = Assert.Single((await LoadStateAsync()).PendingConflicts).Value;

        ConflictResolutionResult result = await new ConflictResolutionService().ResolveAsync(
            _configuration,
            _configPath,
            [pending],
            ConflictChoice.Local);

        Assert.Equal(1, result.AppliedCount);
        Assert.False(File.Exists(cloudPath));
        Assert.Empty((await LoadStateAsync()).PendingConflicts);
        Assert.Single(new BackupManager().List(_configuration.ResolveStatePath(_configPath)));
    }

    private async Task CreateInitialConflictAsync(string path, string localText, string cloudText) {
        await File.WriteAllTextAsync(Path.Combine(_local, path), localText);
        await File.WriteAllTextAsync(Path.Combine(_cloud, path), cloudText);
    }

    private Task<VaultSnapshot> LoadStateAsync() => new SyncStateStore().LoadAsync(
        _configuration.ResolveStatePath(_configPath), _local, _cloud);

    public void Dispose() => Directory.Delete(_root, true);
}
