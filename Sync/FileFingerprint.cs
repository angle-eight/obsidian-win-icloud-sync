namespace ObsidianWinSync.Sync;

public sealed record FileFingerprint(string Hash, long Length, DateTime LastWriteTimeUtc) {
    public bool HasSameContent(FileFingerprint? other) =>
        other is not null && Length == other.Length && string.Equals(Hash, other.Hash, StringComparison.Ordinal);
}
