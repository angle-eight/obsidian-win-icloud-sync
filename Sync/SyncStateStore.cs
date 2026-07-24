using System.Text.Json;

namespace ObsidianWinSync.Sync;

public sealed class SyncStateStore {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true
    };

    public Task<VaultSnapshot> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        LoadAsync(path, null, null, cancellationToken);

    public async Task<VaultSnapshot> LoadAsync(
        string path,
        string? localVaultPath,
        string? icloudVaultPath,
        CancellationToken cancellationToken = default) {
        VaultIdentity? configuredVault = localVaultPath is not null && icloudVaultPath is not null
            ? VaultIdentity.Create(localVaultPath, icloudVaultPath)
            : null;
        try {
            return await LoadCoreAsync(path, configuredVault, cancellationToken);
        } catch (JsonException exception) {
            throw await CreateCorruptionExceptionAsync(path, configuredVault, exception, cancellationToken);
        } catch (InvalidDataException exception) {
            throw await CreateCorruptionExceptionAsync(path, configuredVault, exception, cancellationToken);
        }
    }

    public async Task RecoverFromBackupAsync(
        string path,
        string localVaultPath,
        string icloudVaultPath,
        CancellationToken cancellationToken = default) {
        string fullPath = Path.GetFullPath(path);
        string backupPath = fullPath + ".bak";
        VaultIdentity configuredVault = VaultIdentity.Create(localVaultPath, icloudVaultPath);
        _ = await LoadCoreAsync(backupPath, configuredVault, cancellationToken);

        string timestamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ");
        string corruptCopyPath = fullPath + $".corrupt-{timestamp}";
        string temporaryPath = fullPath + ".recovery.tmp";
        try {
            if (File.Exists(fullPath)) {
                File.Copy(fullPath, corruptCopyPath, false);
            }
            File.Copy(backupPath, temporaryPath, true);
            File.Move(temporaryPath, fullPath, true);
        } finally {
            if (File.Exists(temporaryPath)) {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<VaultSnapshot> LoadCoreAsync(
        string path,
        VaultIdentity? configuredVault,
        CancellationToken cancellationToken) {
        if (!File.Exists(path)) {
            return new VaultSnapshot { Vault = configuredVault };
        }

        await using FileStream stream = File.OpenRead(path);
        VaultSnapshot? snapshot = await JsonSerializer.DeserializeAsync<VaultSnapshot>(stream, JsonOptions, cancellationToken);
        if (snapshot is null || snapshot.Version is < 1 or > 2) {
            throw new InvalidDataException("未対応の同期状態ファイルです。");
        }
        if (snapshot.Files is null || snapshot.Version == 2 && snapshot.PendingConflicts is null) {
            throw new InvalidDataException("同期状態ファイルに必要な項目がありません。");
        }

        if (snapshot.Version == 1) {
            return new VaultSnapshot {
                CreatedAtUtc = snapshot.CreatedAtUtc,
                Vault = configuredVault,
                Files = new Dictionary<string, FileFingerprint>(snapshot.Files, StringComparer.OrdinalIgnoreCase),
                PendingConflicts = new Dictionary<string, PendingConflict>(StringComparer.OrdinalIgnoreCase)
            };
        }

        if (configuredVault is not null) {
            if (snapshot.Vault is null) {
                throw new InvalidDataException("同期状態ファイルにVault識別情報がありません。");
            }
            if (!snapshot.Vault.Matches(configuredVault)) {
                throw new VaultMismatchException(snapshot.Vault, configuredVault);
            }
        }
        return new VaultSnapshot {
            CreatedAtUtc = snapshot.CreatedAtUtc,
            Vault = snapshot.Vault,
            Files = new Dictionary<string, FileFingerprint>(snapshot.Files, StringComparer.OrdinalIgnoreCase),
            PendingConflicts = new Dictionary<string, PendingConflict>(snapshot.PendingConflicts, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static async Task<StateCorruptionException> CreateCorruptionExceptionAsync(
        string path,
        VaultIdentity? configuredVault,
        Exception cause,
        CancellationToken cancellationToken) {
        string fullPath = Path.GetFullPath(path);
        string backupPath = fullPath + ".bak";
        if (!File.Exists(backupPath)) {
            return new StateCorruptionException(fullPath, false, "バックアップファイルがありません。", cause);
        }

        try {
            _ = await LoadCoreAsync(backupPath, configuredVault, cancellationToken);
            return new StateCorruptionException(fullPath, true, null, cause);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception backupException) {
            return new StateCorruptionException(fullPath, false, backupException.Message, cause);
        }
    }

    public async Task SaveAsync(string path, VaultSnapshot snapshot, CancellationToken cancellationToken = default) {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporaryPath = fullPath + ".tmp";
        try {
            await using (FileStream stream = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            if (File.Exists(fullPath)) {
                File.Copy(fullPath, fullPath + ".bak", true);
            }
            File.Move(temporaryPath, fullPath, true);
        } finally {
            if (File.Exists(temporaryPath)) {
                File.Delete(temporaryPath);
            }
        }
    }
}
