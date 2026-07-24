using System.Text.Json;
using ObsidianWinSync.Configuration;

namespace ObsidianWinSync.Tests;

public sealed class ConfigurationTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public ConfigurationTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task LoadAsync_UsesThirtySecondsWhenIntervalIsMissing() {
        string local = Directory.CreateDirectory(Path.Combine(_root, "local")).FullName;
        string cloud = Directory.CreateDirectory(Path.Combine(_root, "cloud")).FullName;
        string path = Path.Combine(_root, "config.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new {
            localVaultPath = local,
            icloudVaultPath = cloud
        }));

        SyncConfiguration configuration = await SyncConfiguration.LoadAsync(path);

        Assert.Equal(30, configuration.IntervalSeconds);
        Assert.Empty(configuration.Validate());
    }

    [Fact]
    public void Validate_RejectsNonPositiveInterval() {
        SyncConfiguration configuration = new() { IntervalSeconds = 0 };
        Assert.Contains(configuration.Validate(), error => error.Contains("intervalSeconds"));
    }

    [Fact]
    public void Defaults_EnableSevenDayBackupAndFourteenDayLogs() {
        SyncConfiguration configuration = new();

        Assert.True(configuration.Backup.Enabled);
        Assert.Equal(7, configuration.Backup.RetentionDays);
        Assert.Equal(1024, configuration.Backup.MaximumSizeMb);
        Assert.Equal(14, configuration.Logging.RetentionDays);
        Assert.False(configuration.Notifications.NotifyOnSuccess);
        Assert.True(configuration.Notifications.NotifyOnConflict);
        Assert.True(configuration.Notifications.NotifyOnError);
        Assert.Equal(300, configuration.Notifications.MinimumIntervalSeconds);
        Assert.False(configuration.StartWithWindows);
        Assert.Equal("state.json", configuration.StateFilePath);
    }

    [Fact]
    public void Validate_RejectsNegativeNotificationInterval() {
        SyncConfiguration configuration = new() {
            Notifications = new NotificationConfiguration { MinimumIntervalSeconds = -1 }
        };

        Assert.Contains(configuration.Validate(), error => error.Contains("minimumIntervalSeconds"));
    }

    [Fact]
    public async Task LoadAsync_ReadsConfiguredIntervalInSeconds() {
        string local = Directory.CreateDirectory(Path.Combine(_root, "local2")).FullName;
        string cloud = Directory.CreateDirectory(Path.Combine(_root, "cloud2")).FullName;
        string path = Path.Combine(_root, "configured.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new {
            localVaultPath = local,
            icloudVaultPath = cloud,
            intervalSeconds = 45
        }));

        SyncConfiguration configuration = await SyncConfiguration.LoadAsync(path);

        Assert.Equal(45, configuration.IntervalSeconds);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
