namespace ObsidianWinSync.Sync;

public sealed class VaultSnapshot {
    public int Version { get; init; } = 2;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public VaultIdentity? Vault { get; init; }
    public Dictionary<string, FileFingerprint> Files { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, PendingConflict> PendingConflicts { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
