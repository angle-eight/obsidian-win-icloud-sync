namespace ObsidianWinSync.Sync;

public sealed class VaultMismatchException : IOException {
    public VaultMismatchException(VaultIdentity stored, VaultIdentity configured)
        : base(
            "同期状態ファイルは別のVaultに関連付けられています。"
            + $" 保存済み: local={stored.LocalPath}, iCloud={stored.IcloudPath};"
            + $" 設定: local={configured.LocalPath}, iCloud={configured.IcloudPath}") {
    }
}
