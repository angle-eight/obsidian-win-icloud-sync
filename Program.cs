using ObsidianWinSync.Configuration;
using ObsidianWinSync.Sync;

return await new Application().RunAsync(args);

internal sealed class Application {
    private readonly FileScanner _scanner = new();
    private readonly SyncCoordinator _coordinator = new();

    public async Task<int> RunAsync(string[] args) {
        if (args.Length == 0 || args.Contains("--help")) {
            PrintHelp();
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        string configPath = GetOption(args, "--config") ?? ConfigurationPathResolver.DefaultFileName;
        try {
            SyncConfiguration configuration = await SyncConfiguration.LoadAsync(configPath);
            IReadOnlyList<string> errors = configuration.Validate();
            if (errors.Count > 0) {
                foreach (string error in errors) {
                    Console.Error.WriteLine($"設定エラー: {error}");
                }
                return 2;
            }

            return command switch {
                "validate" => Validate(configuration),
                "scan" => await ScanAsync(configuration),
                "sync" => await SyncAsync(configuration, configPath, args.Contains("--dry-run")),
                "watch" => await WatchAsync(configuration, configPath),
                "backup" => await BackupAsync(configuration, configPath, args),
                _ => UnknownCommand(command)
            };
        } catch (OperationCanceledException) {
            Console.WriteLine("停止しました。");
            return 0;
        } catch (Exception exception) {
            Console.Error.WriteLine($"エラー: {exception.Message}");
            return 1;
        }
    }

    private static int Validate(SyncConfiguration configuration) {
        Console.WriteLine($"設定は有効です。同期間隔: {configuration.IntervalSeconds}秒");
        return 0;
    }

    private async Task<int> ScanAsync(SyncConfiguration configuration) {
        VaultSnapshot local = await _scanner.ScanAsync(configuration.LocalVaultPath, configuration.ExcludePatterns);
        VaultSnapshot cloud = await _scanner.ScanAsync(configuration.IcloudVaultPath, configuration.ExcludePatterns);
        Console.WriteLine($"local: {local.Files.Count} files");
        Console.WriteLine($"icloud: {cloud.Files.Count} files");
        return 0;
    }

    private async Task<int> SyncAsync(
        SyncConfiguration configuration,
        string configPath,
        bool dryRun,
        CancellationToken cancellationToken = default,
        bool resolveConflictsInteractively = true) {
        Func<SyncAction, CancellationToken, Task<ConflictChoice>>? resolver = dryRun || Console.IsInputRedirected || !resolveConflictsInteractively
            ? null
            : (action, _) => Task.FromResult(ResolveConflict(action, configuration.LocalVaultPath, configuration.IcloudVaultPath));
        SyncRunResult result = await _coordinator.RunAsync(
            configuration,
            configPath,
            dryRun,
            resolver,
            action => Console.WriteLine($"{action.Kind,-24} {action.RelativePath}"),
            Console.IsInputRedirected ? null : ConfirmStateRecoveryAsync,
            cancellationToken);

        if (result.Files.Count == 0) {
            Console.WriteLine("同期済みです。変更はありません。");
        } else {
            TimeSpan duration = result.FinishedAtUtc - result.StartedAtUtc;
            Console.WriteLine($"完了: コピー {result.CopiedCount}, 削除 {result.DeletedCount}, 未解決競合 {result.ConflictCount}, {duration.TotalSeconds:F1}秒");
        }
        return result.HasUnresolvedConflicts ? 3 : 0;
    }

    private static Task<bool> ConfirmStateRecoveryAsync(StateCorruptionException exception, CancellationToken _) {
        Console.Error.WriteLine($"同期状態ファイルが破損しています: {exception.StatePath}");
        Console.Error.WriteLine($"復旧元: {exception.BackupPath}");
        Console.Write("前世代のバックアップから復旧して同期を続けますか？ [y/N]: ");
        bool approved = Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true;
        return Task.FromResult(approved);
    }

    private async Task<int> WatchAsync(SyncConfiguration configuration, string configPath) {
        using CancellationTokenSource cancellation = new();
        Console.CancelKeyPress += (_, eventArgs) => {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.WriteLine($"{configuration.IntervalSeconds}秒間隔で同期します。Ctrl+Cで停止します。");
        while (!cancellation.IsCancellationRequested) {
            int result = await SyncAsync(configuration, configPath, false, cancellation.Token, resolveConflictsInteractively: false);
            if (result != 0) {
                Console.Error.WriteLine($"同期が終了コード {result} で完了しました。");
            }
            await Task.Delay(TimeSpan.FromSeconds(configuration.IntervalSeconds), cancellation.Token);
        }
        return 0;
    }

    private static async Task<int> BackupAsync(SyncConfiguration configuration, string configPath, string[] args) {
        if (args.Length < 2) {
            Console.Error.WriteLine("backup list または backup restore を指定してください。");
            return 2;
        }

        BackupManager manager = new();
        string statePath = configuration.ResolveStatePath(configPath);
        IReadOnlyList<BackupEntry> entries = manager.List(statePath);
        if (args[1].Equals("list", StringComparison.OrdinalIgnoreCase)) {
            if (entries.Count == 0) {
                Console.WriteLine("バックアップはありません。");
            }
            foreach (BackupEntry entry in entries) {
                Console.WriteLine($"{entry.RunId}\t{entry.Side}\t{entry.Length}\t{entry.RelativePath}");
            }
            return 0;
        }

        if (!args[1].Equals("restore", StringComparison.OrdinalIgnoreCase) || args.Length < 5) {
            Console.Error.WriteLine("使い方: backup restore <run-id> <local|icloud> <relative-path> [--force]");
            return 2;
        }

        BackupEntry? selected = entries.FirstOrDefault(entry =>
            entry.RunId.Equals(args[2], StringComparison.Ordinal)
            && entry.Side.Equals(args[3], StringComparison.OrdinalIgnoreCase)
            && entry.RelativePath.Equals(args[4].Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
        if (selected is null) {
            Console.Error.WriteLine("指定されたバックアップが見つかりません。");
            return 2;
        }

        bool overwrite = args.Contains("--force");
        string destinationRoot = selected.Side == "local" ? configuration.LocalVaultPath : configuration.IcloudVaultPath;
        string destination = Path.GetFullPath(Path.Combine(destinationRoot, selected.RelativePath));
        if (File.Exists(destination) && !overwrite) {
            if (Console.IsInputRedirected) {
                Console.Error.WriteLine("復元先が存在します。上書きする場合は --force を指定してください。");
                return 2;
            }
            Console.Write($"既存ファイルを上書きしますか？ {selected.Side}/{selected.RelativePath} [y/N]: ");
            overwrite = Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true;
            if (!overwrite) {
                Console.WriteLine("復元を中止しました。");
                return 0;
            }
        }

        await manager.RestoreAsync(selected, configuration.LocalVaultPath, configuration.IcloudVaultPath, statePath, overwrite);
        Console.WriteLine($"復元しました: {selected.Side}/{selected.RelativePath}");
        return 0;
    }

    private static ConflictChoice ResolveConflict(SyncAction action, string localRoot, string cloudRoot) {
        DisplayDifference(action, localRoot, cloudRoot);
        Console.Write($"競合: {action.RelativePath} [l]ocal / [i]cloud / [s]kip: ");
        return Console.ReadLine()?.Trim().ToLowerInvariant() switch {
            "l" or "local" => ConflictChoice.Local,
            "i" or "icloud" => ConflictChoice.Icloud,
            _ => ConflictChoice.Skip
        };
    }

    private static void DisplayDifference(SyncAction action, string localRoot, string cloudRoot) {
        if (action.Local is null || action.Cloud is null) {
            Console.WriteLine(action.Local is null ? "local: 削除済み / iCloud: 変更あり" : "local: 変更あり / iCloud: 削除済み");
            return;
        }

        string localPath = Path.Combine(localRoot, action.RelativePath);
        string cloudPath = Path.Combine(cloudRoot, action.RelativePath);
        if (!LooksLikeText(localPath) || action.Local.Length > 1_000_000 || action.Cloud.Length > 1_000_000) {
            Console.WriteLine($"バイナリまたは大容量ファイル: local {action.Local.Length} bytes / iCloud {action.Cloud.Length} bytes");
            return;
        }

        try {
            string[] localLines = File.ReadAllLines(localPath);
            string[] cloudLines = File.ReadAllLines(cloudPath);
            Console.WriteLine("--- local");
            Console.WriteLine("+++ iCloud");
            int count = Math.Min(Math.Max(localLines.Length, cloudLines.Length), 40);
            for (int index = 0; index < count; index++) {
                string? localLine = index < localLines.Length ? localLines[index] : null;
                string? cloudLine = index < cloudLines.Length ? cloudLines[index] : null;
                if (!string.Equals(localLine, cloudLine, StringComparison.Ordinal)) {
                    if (localLine is not null) {
                        Console.WriteLine($"- {localLine}");
                    }
                    if (cloudLine is not null) {
                        Console.WriteLine($"+ {cloudLine}");
                    }
                }
            }
            if (Math.Max(localLines.Length, cloudLines.Length) > count) {
                Console.WriteLine("... 差分表示は先頭40行までです");
            }
        } catch (IOException exception) {
            Console.WriteLine($"差分を読み取れません: {exception.Message}");
        }
    }

    private static bool LooksLikeText(string path) {
        string extension = Path.GetExtension(path);
        return extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".yml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".css", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".js", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ts", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetOption(string[] args, string name) {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int UnknownCommand(string command) {
        Console.Error.WriteLine($"不明なコマンドです: {command}");
        PrintHelp();
        return 2;
    }

    private static void PrintHelp() {
        Console.WriteLine("ObsidianWinSync");
        Console.WriteLine("  validate [--config path]");
        Console.WriteLine("  scan [--config path]");
        Console.WriteLine("  sync [--dry-run] [--config path]");
        Console.WriteLine("  watch [--config path]");
        Console.WriteLine("  backup list [--config path]");
        Console.WriteLine("  backup restore <run-id> <local|icloud> <relative-path> [--force] [--config path]");
    }
}
