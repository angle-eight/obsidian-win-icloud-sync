namespace ObsidianWinSync.Configuration;

public static class ConfigurationPathResolver {
    public const string DefaultFileName = "obsidian-win-sync.json";

    public static string ResolveForTray(
        string? explicitPath,
        string executableDirectory,
        string userProfileDirectory,
        string localAppDataDirectory,
        Func<string, bool>? fileExists = null) {
        if (!string.IsNullOrWhiteSpace(explicitPath)) {
            return Path.GetFullPath(explicitPath);
        }

        fileExists ??= File.Exists;
        string[] candidates = [
            Path.Combine(executableDirectory, DefaultFileName),
            Path.Combine(userProfileDirectory, ".obsidian-win-sync", DefaultFileName),
            Path.Combine(localAppDataDirectory, "ObsidianWinSync", DefaultFileName)
        ];
        return candidates.FirstOrDefault(fileExists) ?? candidates[1];
    }
}
