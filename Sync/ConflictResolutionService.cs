using ObsidianWinSync.Configuration;

namespace ObsidianWinSync.Sync;

public sealed record ConflictResolutionResult(int AppliedCount, IReadOnlyList<string> ChangedPaths, IReadOnlyList<string> MissingPaths) {
    public bool RequiresReview => ChangedPaths.Count > 0 || MissingPaths.Count > 0;
}

public sealed class ConflictResolutionService {
    private readonly SyncStateStore _stateStore = new();
    private readonly FileScanner _scanner = new();
    private readonly BackupManager _backupManager = new();
    private readonly SyncExecutor _executor = new();

    public async Task<ConflictResolutionResult> ResolveAsync(
        SyncConfiguration configuration,
        string configurationPath,
        IReadOnlyCollection<PendingConflict> requested,
        ConflictChoice choice,
        CancellationToken cancellationToken = default) {
        if (choice == ConflictChoice.Skip || requested.Count == 0) {
            return new ConflictResolutionResult(0, [], []);
        }

        string statePath = configuration.ResolveStatePath(configurationPath);
        using FileStream syncLock = AcquireLock(statePath + ".lock");
        VaultSnapshot state = await _stateStore.LoadAsync(
            statePath, configuration.LocalVaultPath, configuration.IcloudVaultPath, cancellationToken);
        VaultSnapshot local = await _scanner.ScanAsync(configuration.LocalVaultPath, configuration.ExcludePatterns, cancellationToken);
        VaultSnapshot cloud = await _scanner.ScanAsync(configuration.IcloudVaultPath, configuration.ExcludePatterns, cancellationToken);
        List<string> changed = [];
        List<string> missing = [];
        List<(PendingConflict Pending, SyncAction Action)> actions = [];

        foreach (PendingConflict request in requested) {
            if (!state.PendingConflicts.TryGetValue(request.RelativePath, out PendingConflict? currentPending)) {
                missing.Add(request.RelativePath);
                continue;
            }
            local.Files.TryGetValue(request.RelativePath, out FileFingerprint? localFile);
            cloud.Files.TryGetValue(request.RelativePath, out FileFingerprint? cloudFile);
            SyncAction current = new(request.RelativePath, SyncActionKind.Conflict, localFile, cloudFile, currentPending.Baseline);
            if (!currentPending.HasSameVersions(current) || !request.HasSameVersions(current)) {
                changed.Add(request.RelativePath);
                continue;
            }
            actions.Add((currentPending, ToAction(current, choice)));
        }

        if (changed.Count > 0 || missing.Count > 0) {
            if (changed.Count > 0) {
                VaultSnapshot refreshed = CopyState(state);
                foreach (string path in changed) {
                    if (!state.PendingConflicts.TryGetValue(path, out PendingConflict? pending)) {
                        continue;
                    }
                    local.Files.TryGetValue(path, out FileFingerprint? localFile);
                    cloud.Files.TryGetValue(path, out FileFingerprint? cloudFile);
                    refreshed.PendingConflicts[path] = new PendingConflict(
                        path,
                        pending.DetectedAtUtc,
                        localFile,
                        cloudFile,
                        pending.Baseline);
                }
                await _stateStore.SaveAsync(statePath, refreshed, cancellationToken);
            }
            return new ConflictResolutionResult(0, changed, missing);
        }

        string runId = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmss.fffffffZ");
        foreach ((_, SyncAction action) in actions) {
            await _backupManager.BackupAsync(
                action,
                configuration.LocalVaultPath,
                configuration.IcloudVaultPath,
                statePath,
                configuration.Backup,
                runId,
                cancellationToken);
            await _executor.ExecuteAsync(
                action,
                configuration.LocalVaultPath,
                configuration.IcloudVaultPath,
                cancellationToken);
        }

        VaultSnapshot finalLocal = await _scanner.ScanAsync(configuration.LocalVaultPath, configuration.ExcludePatterns, cancellationToken);
        VaultSnapshot finalCloud = await _scanner.ScanAsync(configuration.IcloudVaultPath, configuration.ExcludePatterns, cancellationToken);
        VaultSnapshot next = CopyState(state);
        foreach ((PendingConflict pending, _) in actions) {
            next.PendingConflicts.Remove(pending.RelativePath);
            finalLocal.Files.TryGetValue(pending.RelativePath, out FileFingerprint? localFile);
            finalCloud.Files.TryGetValue(pending.RelativePath, out FileFingerprint? cloudFile);
            if (localFile is null && cloudFile is null) {
                next.Files.Remove(pending.RelativePath);
            } else if (localFile is not null && localFile.HasSameContent(cloudFile)) {
                next.Files[pending.RelativePath] = localFile;
            } else {
                throw new IOException($"競合解決後に両側が一致しません: {pending.RelativePath}");
            }
        }
        await _stateStore.SaveAsync(statePath, next, cancellationToken);
        return new ConflictResolutionResult(actions.Count, [], []);
    }

    private static SyncAction ToAction(SyncAction current, ConflictChoice choice) => choice switch {
        ConflictChoice.Local when current.Local is null => current with { Kind = SyncActionKind.DeleteCloud },
        ConflictChoice.Local => current with { Kind = SyncActionKind.CopyLocalToCloud },
        ConflictChoice.Icloud when current.Cloud is null => current with { Kind = SyncActionKind.DeleteLocal },
        ConflictChoice.Icloud => current with { Kind = SyncActionKind.CopyCloudToLocal },
        _ => throw new ArgumentOutOfRangeException(nameof(choice))
    };

    private static VaultSnapshot CopyState(VaultSnapshot state) => new() {
        CreatedAtUtc = DateTime.UtcNow,
        Vault = state.Vault,
        Files = new Dictionary<string, FileFingerprint>(state.Files, StringComparer.OrdinalIgnoreCase),
        PendingConflicts = new Dictionary<string, PendingConflict>(state.PendingConflicts, StringComparer.OrdinalIgnoreCase)
    };

    private static FileStream AcquireLock(string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        try {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        } catch (IOException exception) {
            throw new ConcurrentSyncException(exception);
        }
    }
}
