using ObsidianWinSync.Sync;

namespace ObsidianWinSync.Tests;

public sealed class SyncPlannerTests {
    private static readonly FileFingerprint Old = Fingerprint("OLD");
    private static readonly FileFingerprint Local = Fingerprint("LOCAL");
    private static readonly FileFingerprint Cloud = Fingerprint("CLOUD");
    private readonly SyncPlanner _planner = new();

    [Theory]
    [InlineData(false, false, false, false, null)]
    [InlineData(false, false, true, false, SyncActionKind.CopyLocalToCloud)]
    [InlineData(false, false, false, true, SyncActionKind.CopyCloudToLocal)]
    [InlineData(true, true, true, false, SyncActionKind.DeleteLocal)]
    [InlineData(true, true, false, true, SyncActionKind.DeleteCloud)]
    [InlineData(true, true, false, false, SyncActionKind.AlreadySynchronized)]
    public void Create_HandlesCommonChangeCases(
        bool hasBaseline,
        bool localUsesBaseline,
        bool hasLocal,
        bool cloudUsesBaseline,
        SyncActionKind? expected) {
        VaultSnapshot baseline = Snapshot(hasBaseline ? Old : null);
        FileFingerprint? local = hasLocal ? (localUsesBaseline ? Old : Local) : null;
        FileFingerprint? cloud = cloudUsesBaseline ? Old : null;

        SyncPlan plan = _planner.Create(baseline, Snapshot(local), Snapshot(cloud));

        Assert.Equal(expected, plan.Actions.SingleOrDefault()?.Kind);
    }

    [Fact]
    public void Create_MarksDifferentConcurrentEditsAsConflict() {
        SyncPlan plan = _planner.Create(Snapshot(Old), Snapshot(Local), Snapshot(Cloud));
        Assert.Equal(SyncActionKind.Conflict, Assert.Single(plan.Actions).Kind);
    }

    [Fact]
    public void Create_TreatsInitialIdenticalFilesAsSynchronized() {
        SyncPlan plan = _planner.Create(Snapshot(null), Snapshot(Local), Snapshot(Local));
        Assert.Equal(SyncActionKind.AlreadySynchronized, Assert.Single(plan.Actions).Kind);
    }

    [Fact]
    public void Create_TreatsEditAndDeleteAsConflict() {
        SyncPlan plan = _planner.Create(Snapshot(Old), Snapshot(Local), Snapshot(null));
        Assert.Equal(SyncActionKind.Conflict, Assert.Single(plan.Actions).Kind);
    }

    [Fact]
    public void Create_PropagatesSingleSidedDeletion() {
        SyncPlan plan = _planner.Create(Snapshot(Old), Snapshot(null), Snapshot(Old));
        Assert.Equal(SyncActionKind.DeleteCloud, Assert.Single(plan.Actions).Kind);
    }

    private static VaultSnapshot Snapshot(FileFingerprint? fingerprint) {
        VaultSnapshot snapshot = new();
        if (fingerprint is not null) {
            snapshot.Files["note.md"] = fingerprint;
        }
        return snapshot;
    }

    private static FileFingerprint Fingerprint(string value) => new(value, value.Length, DateTime.UnixEpoch);
}
