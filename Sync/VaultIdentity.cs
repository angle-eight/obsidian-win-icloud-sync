using System.Security.Cryptography;
using System.Text;

namespace ObsidianWinSync.Sync;

public sealed record VaultIdentity(string LocalPath, string IcloudPath, string Id) {
    public static VaultIdentity Create(string localPath, string icloudPath) {
        string normalizedLocal = Normalize(localPath);
        string normalizedIcloud = Normalize(icloudPath);
        string value = $"{normalizedLocal.ToUpperInvariant()}\n{normalizedIcloud.ToUpperInvariant()}";
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        return new VaultIdentity(normalizedLocal, normalizedIcloud, id);
    }

    public bool Matches(VaultIdentity other) =>
        string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase)
        && string.Equals(LocalPath, other.LocalPath, StringComparison.OrdinalIgnoreCase)
        && string.Equals(IcloudPath, other.IcloudPath, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
