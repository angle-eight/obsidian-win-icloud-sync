using System.Security.Cryptography;

namespace ObsidianWinSync.Sync;

public sealed class FileScanner {
    public async Task<VaultSnapshot> ScanAsync(
        string rootPath,
        IEnumerable<string> excludePatterns,
        CancellationToken cancellationToken = default) {
        string root = Path.GetFullPath(rootPath);
        GlobMatcher matcher = new(excludePatterns);
        VaultSnapshot snapshot = new();

        foreach (string file in EnumerateSafeFiles(root, cancellationToken)) {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (matcher.IsMatch(relativePath)) {
                continue;
            }

            try {
                FileInfo info = new(file);
                await using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
                snapshot.Files[relativePath] = new FileFingerprint(
                    Convert.ToHexString(hash),
                    info.Length,
                    info.LastWriteTimeUtc);
            } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
                throw new FileScanException(root, relativePath, "ファイル読み取り", exception);
            }
        }

        return snapshot;
    }

    private static IEnumerable<string> EnumerateSafeFiles(string root, CancellationToken cancellationToken) {
        Stack<string> pending = new();
        pending.Push(root);
        while (pending.Count > 0) {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            string relativeDirectory = Path.GetRelativePath(root, directory).Replace('\\', '/');
            if (relativeDirectory == ".") {
                relativeDirectory = "<root>";
            }

            string[] files;
            string[] directories;
            try {
                files = Directory.GetFiles(directory);
                directories = Directory.GetDirectories(directory);
            } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
                throw new FileScanException(root, relativeDirectory, "フォルダ列挙", exception);
            }

            foreach (string file in files) {
                FileAttributes attributes;
                try {
                    attributes = File.GetAttributes(file);
                } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
                    string relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');
                    throw new FileScanException(root, relativePath, "属性読み取り", exception);
                }
                if (!attributes.HasFlag(FileAttributes.ReparsePoint)) {
                    yield return file;
                }
            }

            foreach (string child in directories) {
                FileAttributes attributes;
                try {
                    attributes = File.GetAttributes(child);
                } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
                    string relativePath = Path.GetRelativePath(root, child).Replace('\\', '/');
                    throw new FileScanException(root, relativePath, "属性読み取り", exception);
                }
                if (!attributes.HasFlag(FileAttributes.ReparsePoint)) {
                    pending.Push(child);
                }
            }
        }
    }
}
