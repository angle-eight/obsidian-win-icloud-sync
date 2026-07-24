using ObsidianWinSync.Configuration;

namespace ObsidianWinSync.Sync;

public sealed record BackupEntry(string RunId, string Side, string RelativePath, long Length, DateTime CreatedAtUtc);

public sealed class BackupManager {
    public async Task BackupAsync(
        SyncAction action,
        string localRoot,
        string cloudRoot,
        string statePath,
        BackupConfiguration configuration,
        string runId,
        CancellationToken cancellationToken = default) {
        if (!configuration.Enabled) {
            return;
        }

        (string? source, string? side) = action.Kind switch {
            SyncActionKind.CopyLocalToCloud or SyncActionKind.DeleteCloud => (SafePath(cloudRoot, action.RelativePath), "icloud"),
            SyncActionKind.CopyCloudToLocal or SyncActionKind.DeleteLocal => (SafePath(localRoot, action.RelativePath), "local"),
            _ => (null, null)
        };
        if (source is null || !File.Exists(source)) {
            return;
        }

        string backupRoot = GetBackupRoot(statePath);
        string destination = SafePath(Path.Combine(backupRoot, runId, side!), action.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + $".backup.{Guid.NewGuid():N}.tmp";
        try {
            await using (FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            File.Move(temporary, destination, false);
        } finally {
            if (File.Exists(temporary)) {
                File.Delete(temporary);
            }
        }
        Cleanup(backupRoot, configuration);
    }

    public IReadOnlyList<BackupEntry> List(string statePath) {
        string backupRoot = GetBackupRoot(statePath);
        if (!Directory.Exists(backupRoot)) {
            return [];
        }

        List<BackupEntry> entries = [];
        foreach (string runPath in Directory.EnumerateDirectories(backupRoot)) {
            string runId = Path.GetFileName(runPath);
            foreach (string sidePath in Directory.EnumerateDirectories(runPath)) {
                string side = Path.GetFileName(sidePath);
                foreach (string filePath in Directory.EnumerateFiles(sidePath, "*", SearchOption.AllDirectories)) {
                    FileInfo file = new(filePath);
                    entries.Add(new BackupEntry(
                        runId,
                        side,
                        Path.GetRelativePath(sidePath, filePath).Replace('\\', '/'),
                        file.Length,
                        file.CreationTimeUtc));
                }
            }
        }
        return entries.OrderByDescending(entry => entry.RunId).ThenBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task RestoreAsync(
        BackupEntry entry,
        string localRoot,
        string cloudRoot,
        string statePath,
        bool overwrite,
        CancellationToken cancellationToken = default) {
        if (entry.Side is not ("local" or "icloud")) {
            throw new InvalidDataException($"不明なバックアップ側です: {entry.Side}");
        }

        string backupRoot = GetBackupRoot(statePath);
        string runRoot = SafePath(backupRoot, entry.RunId);
        string sideRoot = SafePath(runRoot, entry.Side);
        string source = SafePath(sideRoot, entry.RelativePath);
        if (!File.Exists(source)) {
            throw new FileNotFoundException("バックアップファイルが見つかりません。", source);
        }

        string destinationRoot = entry.Side == "local" ? localRoot : cloudRoot;
        string destination = SafePath(destinationRoot, entry.RelativePath);
        if (File.Exists(destination) && !overwrite) {
            throw new IOException($"復元先が既に存在します: {destination}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + $".restore.{Guid.NewGuid():N}.tmp";
        try {
            await using (FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            File.Move(temporary, destination, overwrite);
        } finally {
            if (File.Exists(temporary)) {
                File.Delete(temporary);
            }
        }
    }

    private static void Cleanup(string backupRoot, BackupConfiguration configuration) {
        DirectoryInfo root = new(backupRoot);
        DirectoryInfo[] runs = root.EnumerateDirectories().OrderBy(directory => directory.CreationTimeUtc).ToArray();
        DateTime threshold = DateTime.UtcNow.AddDays(-configuration.RetentionDays);
        foreach (DirectoryInfo run in runs.Where(directory => directory.CreationTimeUtc < threshold)) {
            run.Delete(true);
        }

        long maximumBytes = configuration.MaximumSizeMb * 1024L * 1024L;
        runs = root.Exists ? root.EnumerateDirectories().OrderBy(directory => directory.CreationTimeUtc).ToArray() : [];
        long totalBytes = runs.Sum(GetSize);
        foreach (DirectoryInfo run in runs) {
            if (totalBytes <= maximumBytes) {
                break;
            }
            long size = GetSize(run);
            run.Delete(true);
            totalBytes -= size;
        }
    }

    private static long GetSize(DirectoryInfo directory) => directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);

    private static string GetBackupRoot(string statePath) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(statePath))!, "backup");

    private static string SafePath(string root, string relativePath) {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string candidate = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!candidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidDataException($"バックアップ領域外のパスです: {relativePath}");
        }
        return candidate;
    }
}
