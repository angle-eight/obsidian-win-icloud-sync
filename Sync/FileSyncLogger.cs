namespace ObsidianWinSync.Sync;

public sealed class FileSyncLogger {
    private readonly string _logDirectory;
    private readonly int _retentionDays;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileSyncLogger(string statePath, int retentionDays) {
        _logDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(statePath))!, "logs");
        _retentionDays = retentionDays;
    }

    public async Task WriteAsync(string level, string message, CancellationToken cancellationToken = default) {
        await _gate.WaitAsync(cancellationToken);
        try {
            Directory.CreateDirectory(_logDirectory);
            string path = Path.Combine(_logDirectory, $"{DateTime.UtcNow:yyyy-MM-dd}.log");
            string safeLevel = Sanitize(level);
            string safeMessage = Sanitize(message);
            string line = $"{DateTime.UtcNow:O}\t{safeLevel}\t{safeMessage}{Environment.NewLine}";
            await File.AppendAllTextAsync(path, line, cancellationToken);
            Cleanup();
        } finally {
            _gate.Release();
        }
    }

    private static string Sanitize(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

    private void Cleanup() {
        DateTime threshold = DateTime.UtcNow.AddDays(-_retentionDays);
        foreach (string path in Directory.EnumerateFiles(_logDirectory, "*.log")) {
            if (File.GetLastWriteTimeUtc(path) < threshold) {
                File.Delete(path);
            }
        }
    }
}
