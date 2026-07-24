namespace ObsidianWinSync.Sync;

public sealed class SyncPlanner {
    public SyncPlan Create(VaultSnapshot baseline, VaultSnapshot local, VaultSnapshot cloud) {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        paths.UnionWith(baseline.Files.Keys);
        paths.UnionWith(local.Files.Keys);
        paths.UnionWith(cloud.Files.Keys);

        List<SyncAction> actions = [];
        foreach (string path in paths.Order(StringComparer.OrdinalIgnoreCase)) {
            baseline.Files.TryGetValue(path, out FileFingerprint? previous);
            local.Files.TryGetValue(path, out FileFingerprint? localFile);
            cloud.Files.TryGetValue(path, out FileFingerprint? cloudFile);
            SyncActionKind? kind = Decide(previous, localFile, cloudFile);
            if (kind is not null) {
                actions.Add(new SyncAction(path, kind.Value, localFile, cloudFile, previous));
            }
        }

        return new SyncPlan(actions);
    }

    private static SyncActionKind? Decide(
        FileFingerprint? baseline,
        FileFingerprint? local,
        FileFingerprint? cloud) {
        if (baseline is null) {
            if (local is null && cloud is null) {
                return null;
            }
            if (local is not null && cloud is null) {
                return SyncActionKind.CopyLocalToCloud;
            }
            if (local is null) {
                return SyncActionKind.CopyCloudToLocal;
            }
            return local.HasSameContent(cloud)
                ? SyncActionKind.AlreadySynchronized
                : SyncActionKind.Conflict;
        }

        bool localChanged = !Same(local, baseline);
        bool cloudChanged = !Same(cloud, baseline);
        if (!localChanged && !cloudChanged) {
            return null;
        }
        if (localChanged && !cloudChanged) {
            return local is null ? SyncActionKind.DeleteCloud : SyncActionKind.CopyLocalToCloud;
        }
        if (!localChanged) {
            return cloud is null ? SyncActionKind.DeleteLocal : SyncActionKind.CopyCloudToLocal;
        }
        if (local is null && cloud is null) {
            return SyncActionKind.AlreadySynchronized;
        }
        return local is not null && local.HasSameContent(cloud)
            ? SyncActionKind.AlreadySynchronized
            : SyncActionKind.Conflict;
    }

    private static bool Same(FileFingerprint? left, FileFingerprint? right) =>
        left is null ? right is null : left.HasSameContent(right);
}
