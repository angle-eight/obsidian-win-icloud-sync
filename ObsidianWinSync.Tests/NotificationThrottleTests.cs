using ObsidianWinSync.Sync;

namespace ObsidianWinSync.Tests;

public sealed class NotificationThrottleTests {
    [Fact]
    public void ShouldShow_SuppressesSameKeyUntilIntervalExpires() {
        DateTime now = DateTime.UnixEpoch;
        NotificationThrottle throttle = new(() => now);

        Assert.True(throttle.ShouldShow("same error", TimeSpan.FromMinutes(5)));
        now = now.AddMinutes(4);
        Assert.False(throttle.ShouldShow("same error", TimeSpan.FromMinutes(5)));
        Assert.True(throttle.ShouldShow("different error", TimeSpan.FromMinutes(5)));
        now = now.AddMinutes(1);
        Assert.True(throttle.ShouldShow("same error", TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void ShouldShow_ZeroIntervalDisablesSuppression() {
        NotificationThrottle throttle = new(() => DateTime.UnixEpoch);

        Assert.True(throttle.ShouldShow("error", TimeSpan.Zero));
        Assert.True(throttle.ShouldShow("error", TimeSpan.Zero));
    }
}
