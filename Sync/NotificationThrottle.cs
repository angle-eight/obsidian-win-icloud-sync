namespace ObsidianWinSync.Sync;

public sealed class NotificationThrottle {
    private readonly Func<DateTime> _utcNow;
    private readonly Dictionary<string, DateTime> _lastShown = new(StringComparer.Ordinal);

    public NotificationThrottle(Func<DateTime>? utcNow = null) {
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public bool ShouldShow(string key, TimeSpan minimumInterval) {
        DateTime now = _utcNow();
        if (minimumInterval > TimeSpan.Zero
            && _lastShown.TryGetValue(key, out DateTime lastShown)
            && now - lastShown < minimumInterval) {
            return false;
        }
        _lastShown[key] = now;
        if (_lastShown.Count > 256) {
            foreach (string oldKey in _lastShown.OrderBy(pair => pair.Value).Take(_lastShown.Count - 256).Select(pair => pair.Key).ToArray()) {
                _lastShown.Remove(oldKey);
            }
        }
        return true;
    }
}
