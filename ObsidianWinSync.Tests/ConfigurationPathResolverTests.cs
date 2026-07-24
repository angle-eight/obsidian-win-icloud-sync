using ObsidianWinSync.Configuration;

namespace ObsidianWinSync.Tests;

public sealed class ConfigurationPathResolverTests {
    [Fact]
    public void ResolveForTray_SearchesInExpectedOrder() {
        string executable = Path.Combine("C:", "app");
        string user = Path.Combine("C:", "Users", "test");
        string localAppData = Path.Combine(user, "AppData", "Local");
        string expected = Path.Combine(user, ".obsidian-win-sync", ConfigurationPathResolver.DefaultFileName);

        string actual = ConfigurationPathResolver.ResolveForTray(null, executable, user, localAppData, path => path == expected);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveForTray_PrefersExecutableDirectory() {
        string executable = Path.Combine("C:", "app");
        string expected = Path.Combine(executable, ConfigurationPathResolver.DefaultFileName);

        string actual = ConfigurationPathResolver.ResolveForTray(null, executable, "user", "local", path => true);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveForTray_UsesUserDirectoryWhenNoFileExists() {
        string user = Path.Combine("C:", "Users", "test");

        string actual = ConfigurationPathResolver.ResolveForTray(null, "app", user, "local", _ => false);

        Assert.Equal(Path.Combine(user, ".obsidian-win-sync", ConfigurationPathResolver.DefaultFileName), actual);
    }

    [Fact]
    public void ResolveForTray_FindsLocalAppDataAfterUserDirectory() {
        string localAppData = Path.Combine("C:", "Users", "test", "AppData", "Local");
        string expected = Path.Combine(localAppData, "ObsidianWinSync", ConfigurationPathResolver.DefaultFileName);

        string actual = ConfigurationPathResolver.ResolveForTray(null, "app", "user", localAppData, path => path == expected);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveForTray_UsesExplicitPathWithoutSearching() {
        string relative = Path.Combine("settings", "custom.json");

        string actual = ConfigurationPathResolver.ResolveForTray(relative, "app", "user", "local", _ => throw new InvalidOperationException());

        Assert.Equal(Path.GetFullPath(relative), actual);
    }
}
