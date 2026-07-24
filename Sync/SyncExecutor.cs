namespace ObsidianWinSync.Sync;

public enum SyncExecutionStage {
    BeforeCopy,
    AfterTemporaryCopy,
    BeforeReplace,
    BeforeDelete
}

public sealed class SyncExecutor {
    private readonly Func<SyncExecutionStage, string, CancellationToken, Task>? _faultInjector;

    public SyncExecutor(Func<SyncExecutionStage, string, CancellationToken, Task>? faultInjector = null) {
        _faultInjector = faultInjector;
    }
    public async Task ExecuteAsync(
        SyncAction action,
        string localRoot,
        string cloudRoot,
        CancellationToken cancellationToken = default) {
        switch (action.Kind) {
            case SyncActionKind.CopyLocalToCloud:
                await CopyAtomicallyAsync(SafePath(localRoot, action.RelativePath), SafePath(cloudRoot, action.RelativePath), cancellationToken);
                break;
            case SyncActionKind.CopyCloudToLocal:
                await CopyAtomicallyAsync(SafePath(cloudRoot, action.RelativePath), SafePath(localRoot, action.RelativePath), cancellationToken);
                break;
            case SyncActionKind.DeleteLocal:
                await DeleteIfPresentAsync(SafePath(localRoot, action.RelativePath), cancellationToken);
                break;
            case SyncActionKind.DeleteCloud:
                await DeleteIfPresentAsync(SafePath(cloudRoot, action.RelativePath), cancellationToken);
                break;
        }
    }

    private async Task CopyAtomicallyAsync(string source, string destination, CancellationToken cancellationToken) {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + $".obsidianwinsync.{Guid.NewGuid():N}.tmp";
        try {
            const int attempts = 3;
            for (int attempt = 1; ; attempt++) {
                try {
                    await InjectAsync(SyncExecutionStage.BeforeCopy, source, cancellationToken);
                    await using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    await using FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    await input.CopyToAsync(output, cancellationToken);
                    await output.FlushAsync(cancellationToken);
                    await InjectAsync(SyncExecutionStage.AfterTemporaryCopy, temporary, cancellationToken);
                    break;
                } catch (IOException) when (attempt < attempts) {
                    if (File.Exists(temporary)) {
                        File.Delete(temporary);
                    }
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                }
            }
            await InjectAsync(SyncExecutionStage.BeforeReplace, destination, cancellationToken);
            File.Move(temporary, destination, true);
            File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
        } finally {
            if (File.Exists(temporary)) {
                File.Delete(temporary);
            }
        }
    }

    private static string SafePath(string root, string relativePath) {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string candidate = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!candidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidDataException($"Vault外のパスは処理できません: {relativePath}");
        }
        return candidate;
    }

    private async Task DeleteIfPresentAsync(string path, CancellationToken cancellationToken) {
        if (File.Exists(path)) {
            await InjectAsync(SyncExecutionStage.BeforeDelete, path, cancellationToken);
            File.Delete(path);
        }
    }

    private Task InjectAsync(SyncExecutionStage stage, string path, CancellationToken cancellationToken) =>
        _faultInjector?.Invoke(stage, path, cancellationToken) ?? Task.CompletedTask;
}
