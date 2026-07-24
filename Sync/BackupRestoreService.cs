using ObsidianWinSync.Configuration;

namespace ObsidianWinSync.Sync;

public sealed class BackupRestoreService {
    private readonly BackupManager _manager = new();

    public bool DestinationExists(BackupEntry entry, SyncConfiguration configuration) =>
        File.Exists(GetDestination(entry, configuration));

    public async Task RestoreAsync(
        BackupEntry entry,
        SyncConfiguration configuration,
        string configurationPath,
        bool overwrite,
        CancellationToken cancellationToken = default) {
        string statePath = configuration.ResolveStatePath(configurationPath);
        using FileStream syncLock = AcquireLock(statePath + ".lock");
        if (overwrite && DestinationExists(entry, configuration)) {
            SyncActionKind backupAction = entry.Side == "local"
                ? SyncActionKind.DeleteLocal
                : SyncActionKind.DeleteCloud;
            await _manager.BackupAsync(
                new SyncAction(entry.RelativePath, backupAction, null, null, null),
                configuration.LocalVaultPath,
                configuration.IcloudVaultPath,
                statePath,
                configuration.Backup,
                $"restore-{DateTime.UtcNow:yyyy-MM-ddTHHmmss.fffffffZ}",
                cancellationToken);
        }
        await _manager.RestoreAsync(
            entry,
            configuration.LocalVaultPath,
            configuration.IcloudVaultPath,
            statePath,
            overwrite,
            cancellationToken);
    }

    private static string GetDestination(BackupEntry entry, SyncConfiguration configuration) {
        string root = entry.Side switch {
            "local" => configuration.LocalVaultPath,
            "icloud" => configuration.IcloudVaultPath,
            _ => throw new InvalidDataException($"不明なバックアップ側です: {entry.Side}")
        };
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string destination = Path.GetFullPath(Path.Combine(normalizedRoot, entry.RelativePath));
        if (!destination.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidDataException($"Vault外の復元先です: {entry.RelativePath}");
        }
        return destination;
    }

    private static FileStream AcquireLock(string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        try {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        } catch (IOException exception) {
            throw new ConcurrentSyncException(exception);
        }
    }
}
