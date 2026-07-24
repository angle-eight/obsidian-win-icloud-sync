using ObsidianWinSync.Configuration;

namespace ObsidianWinSync.Sync;

public sealed class SyncCoordinator {
    private readonly FileScanner _scanner;
    private readonly SyncPlanner _planner;
    private readonly SyncExecutor _executor;
    private readonly SyncStateStore _stateStore;
    private readonly BackupManager _backupManager;

    public SyncCoordinator(FileScanner? scanner = null, SyncPlanner? planner = null, SyncExecutor? executor = null, SyncStateStore? stateStore = null, BackupManager? backupManager = null) {
        _scanner = scanner ?? new FileScanner();
        _planner = planner ?? new SyncPlanner();
        _executor = executor ?? new SyncExecutor();
        _stateStore = stateStore ?? new SyncStateStore();
        _backupManager = backupManager ?? new BackupManager();
    }

    public async Task<SyncRunResult> RunAsync(
        SyncConfiguration configuration,
        string configurationPath,
        bool dryRun = false,
        Func<SyncAction, CancellationToken, Task<ConflictChoice>>? conflictResolver = null,
        Action<SyncAction>? actionObserver = null,
        Func<StateCorruptionException, CancellationToken, Task<bool>>? stateRecoveryResolver = null,
        CancellationToken cancellationToken = default) {
        DateTime startedAtUtc = DateTime.UtcNow;
        string statePath = configuration.ResolveStatePath(configurationPath);
        string runId = startedAtUtc.ToString("yyyy-MM-ddTHHmmss.fffffffZ");
        FileSyncLogger logger = new(statePath, configuration.Logging.RetentionDays);
        try {
            await logger.WriteAsync("Information", $"event=sync_started run={runId} dryRun={dryRun}", cancellationToken);
            using FileStream? syncLock = dryRun ? null : AcquireLock(statePath + ".lock");
            VaultSnapshot baseline;
            try {
                baseline = await _stateStore.LoadAsync(
                    statePath,
                    configuration.LocalVaultPath,
                    configuration.IcloudVaultPath,
                    cancellationToken);
            } catch (StateCorruptionException exception) when (exception.BackupAvailable && stateRecoveryResolver is not null) {
                bool approved = await stateRecoveryResolver(exception, cancellationToken);
                if (!approved) {
                    throw;
                }
                await _stateStore.RecoverFromBackupAsync(
                    statePath,
                    configuration.LocalVaultPath,
                    configuration.IcloudVaultPath,
                    cancellationToken);
                await logger.WriteAsync("Warning", $"event=state_restored run={runId} backup={exception.BackupPath}", cancellationToken);
                baseline = await _stateStore.LoadAsync(
                    statePath,
                    configuration.LocalVaultPath,
                    configuration.IcloudVaultPath,
                    cancellationToken);
            }
            VaultSnapshot local = await _scanner.ScanAsync(configuration.LocalVaultPath, configuration.ExcludePatterns, cancellationToken);
            VaultSnapshot cloud = await _scanner.ScanAsync(configuration.IcloudVaultPath, configuration.ExcludePatterns, cancellationToken);
            SyncPlan plan = _planner.Create(baseline, local, cloud);
            List<SyncFileResult> results = [];

            foreach (SyncAction original in plan.Actions) {
                actionObserver?.Invoke(original);
                if (original.Kind == SyncActionKind.Conflict) {
                    await logger.WriteAsync("Warning", $"event=conflict_detected run={runId} path={original.RelativePath}", cancellationToken);
                }
                SyncAction? action = await ResolveAsync(original, conflictResolver, cancellationToken);
                if (original.Kind == SyncActionKind.Conflict) {
                    string resolution = action?.Kind.ToString() ?? "Unresolved";
                    await logger.WriteAsync("Warning", $"event=conflict_resolution run={runId} path={original.RelativePath} resolution={resolution}", cancellationToken);
                }
                if (action is null) {
                    results.Add(new SyncFileResult(original.RelativePath, original.Kind, null));
                    continue;
                }
                if (!dryRun) {
                    await _backupManager.BackupAsync(action, configuration.LocalVaultPath, configuration.IcloudVaultPath, statePath, configuration.Backup, runId, cancellationToken);
                    await _executor.ExecuteAsync(action, configuration.LocalVaultPath, configuration.IcloudVaultPath, cancellationToken);
                    await logger.WriteAsync("Information", $"event=file_applied run={runId} action={action.Kind} path={action.RelativePath}", cancellationToken);
                }
                results.Add(new SyncFileResult(original.RelativePath, original.Kind, dryRun ? null : action.Kind));
            }

            if (!dryRun) {
                VaultSnapshot finalLocal = await _scanner.ScanAsync(configuration.LocalVaultPath, configuration.ExcludePatterns, cancellationToken);
                VaultSnapshot finalCloud = await _scanner.ScanAsync(configuration.IcloudVaultPath, configuration.ExcludePatterns, cancellationToken);
                await _stateStore.SaveAsync(
                    statePath,
                    BuildNextState(baseline, finalLocal, finalCloud, plan, results, configuration.LocalVaultPath, configuration.IcloudVaultPath),
                    cancellationToken);
            }
            SyncRunResult result = new(startedAtUtc, DateTime.UtcNow, dryRun, results);
            await logger.WriteAsync("Information", $"event=sync_finished run={runId} copied={result.CopiedCount} deleted={result.DeletedCount} conflicts={result.ConflictCount}", cancellationToken);
            await TryAppendHistoryAsync(statePath, new SyncHistoryEntry(
                runId,
                startedAtUtc,
                result.FinishedAtUtc,
                result.HasUnresolvedConflicts ? SyncHistoryStatus.Conflicts : SyncHistoryStatus.Success,
                dryRun,
                result.CopiedCount,
                result.DeletedCount,
                result.ConflictCount,
                null,
                null));
            return result;
        } catch (OperationCanceledException exception) {
            await TryWriteFailureAsync(logger, "Information", "sync_cancelled", runId, exception);
            await TryAppendHistoryAsync(statePath, new SyncHistoryEntry(
                runId, startedAtUtc, DateTime.UtcNow, SyncHistoryStatus.Cancelled, dryRun, 0, 0, 0, "cancelled", exception.Message));
            throw;
        } catch (Exception exception) {
            SyncFailureClassification classification = SyncFailureClassifier.Classify(exception);
            await TryWriteFailureAsync(logger, "Error", "sync_failed", runId, exception, classification);
            await TryAppendHistoryAsync(statePath, new SyncHistoryEntry(
                runId, startedAtUtc, DateTime.UtcNow, SyncHistoryStatus.Failed, dryRun, 0, 0, 0, classification.Code, exception.Message));
            throw;
        }
    }

    private static async Task<SyncAction?> ResolveAsync(SyncAction action, Func<SyncAction, CancellationToken, Task<ConflictChoice>>? resolver, CancellationToken cancellationToken) {
        if (action.Kind != SyncActionKind.Conflict) {
            return action;
        }
        if (resolver is null) {
            return null;
        }
        ConflictChoice choice = await resolver(action, cancellationToken);
        return choice switch {
            ConflictChoice.Local when action.Local is null => action with { Kind = SyncActionKind.DeleteCloud },
            ConflictChoice.Local => action with { Kind = SyncActionKind.CopyLocalToCloud },
            ConflictChoice.Icloud when action.Cloud is null => action with { Kind = SyncActionKind.DeleteLocal },
            ConflictChoice.Icloud => action with { Kind = SyncActionKind.CopyCloudToLocal },
            _ => null
        };
    }

    private static VaultSnapshot BuildNextState(
        VaultSnapshot previous,
        VaultSnapshot local,
        VaultSnapshot cloud,
        SyncPlan plan,
        IReadOnlyList<SyncFileResult> results,
        string localVaultPath,
        string icloudVaultPath) {
        VaultSnapshot next = new() { Vault = VaultIdentity.Create(localVaultPath, icloudVaultPath) };
        HashSet<string> paths = new(local.Files.Keys, StringComparer.OrdinalIgnoreCase);
        paths.UnionWith(cloud.Files.Keys);
        paths.UnionWith(previous.Files.Keys);
        foreach (string path in paths) {
            local.Files.TryGetValue(path, out FileFingerprint? localFile);
            cloud.Files.TryGetValue(path, out FileFingerprint? cloudFile);
            if (localFile is not null && localFile.HasSameContent(cloudFile)) {
                next.Files[path] = localFile;
            } else if (previous.Files.TryGetValue(path, out FileFingerprint? baseline)) {
                next.Files[path] = baseline;
            }
        }

        HashSet<string> unresolvedPaths = new(
            results.Where(result => result.PlannedAction == SyncActionKind.Conflict && result.AppliedAction is null)
                .Select(result => result.RelativePath),
            StringComparer.OrdinalIgnoreCase);
        foreach (SyncAction conflict in plan.Actions.Where(action =>
                     action.Kind == SyncActionKind.Conflict && unresolvedPaths.Contains(action.RelativePath))) {
            DateTime detectedAtUtc = previous.PendingConflicts.TryGetValue(conflict.RelativePath, out PendingConflict? pending)
                && pending.HasSameVersions(conflict)
                    ? pending.DetectedAtUtc
                    : DateTime.UtcNow;
            next.PendingConflicts[conflict.RelativePath] = new PendingConflict(
                conflict.RelativePath,
                detectedAtUtc,
                conflict.Local,
                conflict.Cloud,
                conflict.Baseline);
        }
        return next;
    }

    private static FileStream AcquireLock(string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        try {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        } catch (IOException exception) {
            throw new ConcurrentSyncException(exception);
        }
    }

    private static async Task TryWriteFailureAsync(
        FileSyncLogger logger,
        string level,
        string eventName,
        string runId,
        Exception exception,
        SyncFailureClassification? classification = null) {
        classification ??= SyncFailureClassifier.Classify(exception);
        string path = exception is FileScanException scan ? scan.RelativePath : "-";
        try {
            await logger.WriteAsync(
                level,
                $"event={eventName} run={runId} code={classification.Code} transient={classification.IsTransient} path={path} exception={exception.GetType().Name} message={exception.Message}",
                CancellationToken.None);
        } catch {
            // Preserve the original sync failure when logging itself is unavailable.
        }
    }

    private static async Task TryAppendHistoryAsync(string statePath, SyncHistoryEntry entry) {
        try {
            await new SyncHistoryStore().AppendAsync(statePath, entry, CancellationToken.None);
        } catch {
            // History is diagnostic data and must not change the sync outcome.
        }
    }
}
