namespace ObsidianWinSync.Sync;

public sealed class FileScanException : IOException {
    public FileScanException(string rootPath, string relativePath, string operation, Exception innerException)
        : base($"Vaultのスキャンに失敗しました ({operation}): {relativePath}", innerException) {
        RootPath = rootPath;
        RelativePath = relativePath;
        Operation = operation;
    }

    public string RootPath { get; }
    public string RelativePath { get; }
    public string Operation { get; }
}
