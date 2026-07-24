using System.Text.Json;

namespace ObsidianWinSync.Configuration;

public sealed class SyncConfiguration {
    public string LocalVaultPath { get; init; } = "";
    public string IcloudVaultPath { get; init; } = "";
    public int IntervalSeconds { get; init; } = 30;
    public string[] ExcludePatterns { get; init; } = [];
    public string StateFilePath { get; init; } = "state.json";
    public BackupConfiguration Backup { get; init; } = new();
    public LoggingConfiguration Logging { get; init; } = new();
    public NotificationConfiguration Notifications { get; init; } = new();
    public bool StartWithWindows { get; init; }

    public static async Task<SyncConfiguration> LoadAsync(string path, CancellationToken cancellationToken = default) {
        await using FileStream stream = File.OpenRead(path);
        SyncConfiguration? configuration = await JsonSerializer.DeserializeAsync<SyncConfiguration>(
            stream,
            JsonOptions,
            cancellationToken);

        return configuration ?? throw new InvalidDataException("設定JSONが空です。");
    }

    public IReadOnlyList<string> Validate() {
        List<string> errors = [];
        if (IntervalSeconds <= 0 || IntervalSeconds > int.MaxValue / 1000) {
            errors.Add($"intervalSeconds は1以上{int.MaxValue / 1000}以下の秒数を指定してください。");
        }
        if (Backup.RetentionDays < 0 || Backup.MaximumSizeMb <= 0) {
            errors.Add("backup の retentionDays は0以上、maximumSizeMb は1以上を指定してください。");
        }
        if (Logging.RetentionDays < 1) {
            errors.Add("logging.retentionDays は1以上を指定してください。");
        }
        if (Notifications.MinimumIntervalSeconds < 0 || Notifications.MinimumIntervalSeconds > int.MaxValue / 1000) {
            errors.Add($"notifications.minimumIntervalSeconds は0以上{int.MaxValue / 1000}以下を指定してください。");
        }

        ValidateDirectory(LocalVaultPath, "localVaultPath", errors);
        ValidateDirectory(IcloudVaultPath, "icloudVaultPath", errors);

        if (errors.Count == 0) {
            string local = NormalizeDirectory(LocalVaultPath);
            string cloud = NormalizeDirectory(IcloudVaultPath);
            if (string.Equals(local, cloud, StringComparison.OrdinalIgnoreCase)) {
                errors.Add("localVaultPath と icloudVaultPath は別のフォルダを指定してください。");
            } else if (IsInside(local, cloud) || IsInside(cloud, local)) {
                errors.Add("同期フォルダ同士を包含関係にすることはできません。");
            }
        }

        return errors;
    }

    public string ResolveStatePath(string configurationPath) {
        if (Path.IsPathFullyQualified(StateFilePath)) {
            return Path.GetFullPath(StateFilePath);
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(configurationPath))!;
        return Path.GetFullPath(Path.Combine(directory, StateFilePath));
    }

    private static void ValidateDirectory(string path, string propertyName, ICollection<string> errors) {
        if (string.IsNullOrWhiteSpace(path)) {
            errors.Add($"{propertyName} は必須です。");
        } else if (!Directory.Exists(path)) {
            errors.Add($"{propertyName} のフォルダが存在しません: {path}");
        }
    }

    private static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsInside(string candidate, string parent) =>
        candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true
    };
}

public sealed class BackupConfiguration {
    public bool Enabled { get; init; } = true;
    public int RetentionDays { get; init; } = 7;
    public int MaximumSizeMb { get; init; } = 1024;
}

public sealed class LoggingConfiguration {
    public int RetentionDays { get; init; } = 14;
}

public sealed class NotificationConfiguration {
    public bool NotifyOnSuccess { get; init; }
    public bool NotifyOnConflict { get; init; } = true;
    public bool NotifyOnError { get; init; } = true;
    public int MinimumIntervalSeconds { get; init; } = 300;
}
