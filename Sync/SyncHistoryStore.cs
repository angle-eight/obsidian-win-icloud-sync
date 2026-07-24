using System.Text.Json;

namespace ObsidianWinSync.Sync;

public enum SyncHistoryStatus {
    Success,
    Conflicts,
    Cancelled,
    Failed
}

public sealed record SyncHistoryEntry(
    string RunId,
    DateTime StartedAtUtc,
    DateTime FinishedAtUtc,
    SyncHistoryStatus Status,
    bool IsDryRun,
    int CopiedCount,
    int DeletedCount,
    int ConflictCount,
    string? ErrorCode,
    string? ErrorMessage);

public sealed class SyncHistoryStore {
    public const int MaximumEntries = 100;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string GetPath(string statePath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(statePath))!, "sync-history.json");

    public async Task<IReadOnlyList<SyncHistoryEntry>> LoadAsync(
        string statePath,
        CancellationToken cancellationToken = default) {
        string path = GetPath(statePath);
        if (!File.Exists(path)) {
            return [];
        }
        await using FileStream stream = File.OpenRead(path);
        List<SyncHistoryEntry>? entries = await JsonSerializer.DeserializeAsync<List<SyncHistoryEntry>>(
            stream, JsonOptions, cancellationToken);
        return entries ?? [];
    }

    public async Task AppendAsync(
        string statePath,
        SyncHistoryEntry entry,
        CancellationToken cancellationToken = default) {
        string path = GetPath(statePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        List<SyncHistoryEntry> entries = (await LoadAsync(statePath, cancellationToken)).ToList();
        entries.Insert(0, entry);
        if (entries.Count > MaximumEntries) {
            entries.RemoveRange(MaximumEntries, entries.Count - MaximumEntries);
        }

        string temporaryPath = path + ".tmp";
        try {
            await using (FileStream stream = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
                await JsonSerializer.SerializeAsync(stream, entries, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, path, true);
        } finally {
            if (File.Exists(temporaryPath)) {
                File.Delete(temporaryPath);
            }
        }
    }
}
