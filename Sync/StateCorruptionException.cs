namespace ObsidianWinSync.Sync;

public sealed class StateCorruptionException : IOException {
    public StateCorruptionException(string statePath, bool backupAvailable, string? backupError, Exception innerException)
        : base(CreateMessage(statePath, backupAvailable, backupError), innerException) {
        StatePath = statePath;
        BackupPath = statePath + ".bak";
        BackupAvailable = backupAvailable;
        BackupError = backupError;
    }

    public string StatePath { get; }
    public string BackupPath { get; }
    public bool BackupAvailable { get; }
    public string? BackupError { get; }

    private static string CreateMessage(string path, bool backupAvailable, string? backupError) {
        string message = $"同期状態ファイルが破損しています: {path}";
        if (backupAvailable) {
            return message + "。前世代のバックアップから復旧できます。";
        }
        return backupError is null ? message : message + $"。バックアップも利用できません: {backupError}";
    }
}
