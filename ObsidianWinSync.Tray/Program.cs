using ObsidianWinSync.Configuration;
using ObsidianWinSync.Sync;
using Microsoft.Win32;

namespace ObsidianWinSync.Tray;

internal static class Program {
    [STAThread]
    private static void Main(string[] args) {
        using Mutex mutex = new(true, "Local\\ObsidianWinSync.Tray", out bool createdNew);
        if (!createdNew) {
            MessageBox.Show("ObsidianWinSync は既に起動しています。", "ObsidianWinSync", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string configPath = ConfigurationPathResolver.ResolveForTray(
            GetOption(args, "--config"),
            Path.GetDirectoryName(Application.ExecutablePath)!,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        Application.Run(new TrayApplicationContext(configPath));
    }

    private static string? GetOption(string[] args, string name) {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

internal sealed class TrayApplicationContext : ApplicationContext {
    private readonly string _configPath;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _conflictsItem;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly CancellationTokenSource _cancellation = new();
    private readonly NotificationThrottle _notificationThrottle = new();
    private bool _paused;
    private bool _notifyOnError = true;
    private int _intervalSeconds = 30;
    private string _currentStatus = "起動中";
    private DateTime? _nextSyncAt;
    private int _notificationMinimumIntervalSeconds = 300;

    public TrayApplicationContext(string configPath) {
        _configPath = configPath;
        _pauseItem = new ToolStripMenuItem("同期を一時停止", null, (_, _) => TogglePause());
        _conflictsItem = new ToolStripMenuItem("競合を確認", null, async (_, _) => await ShowConflictsAsync());
        ContextMenuStrip menu = new();
        menu.Items.Add("今すぐ同期", null, async (_, _) => await SyncNowAsync());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_conflictsItem);
        menu.Items.Add("同期状態と履歴", null, async (_, _) => await ShowStatusHistoryAsync());
        menu.Items.Add("バックアップを復元", null, async (_, _) => await ShowBackupsAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("設定", null, async (_, _) => await ShowSettingsAsync());
        menu.Items.Add("設定ファイルを開く", null, async (_, _) => await OpenConfigAsync());
        menu.Items.Add("ログフォルダを開く", null, (_, _) => OpenLogs());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, async (_, _) => await ExitAsync());

        Icon applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        _icon = new NotifyIcon {
            Icon = applicationIcon,
            Text = "ObsidianWinSync - 起動中",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += async (_, _) => await SyncNowAsync();

        _timer = new System.Windows.Forms.Timer { Interval = _intervalSeconds * 1000 };
        _timer.Tick += async (_, _) => await SyncNowAsync();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync() {
        if (!File.Exists(_configPath)) {
            SetStatus("初期設定が必要");
            await ShowSettingsAsync(syncAfterSave: false);
            if (!File.Exists(_configPath)) {
                return;
            }
        }
        _timer.Start();
        _nextSyncAt = DateTime.Now;
        await SyncNowAsync();
    }

    private async Task SyncNowAsync() {
        if (!File.Exists(_configPath)) {
            SetStatus("初期設定が必要");
            await ShowSettingsAsync();
            return;
        }
        if (_paused || !await _syncGate.WaitAsync(0)) {
            return;
        }
        try {
            SetStatus("同期中");
            SyncConfiguration configuration = await SyncConfiguration.LoadAsync(_configPath, _cancellation.Token);
            _notifyOnError = configuration.Notifications.NotifyOnError;
            _notificationMinimumIntervalSeconds = configuration.Notifications.MinimumIntervalSeconds;
            IReadOnlyList<string> errors = configuration.Validate();
            if (errors.Count > 0) {
                throw new InvalidDataException(string.Join(Environment.NewLine, errors));
            }
            _intervalSeconds = configuration.IntervalSeconds;
            _timer.Interval = checked(_intervalSeconds * 1000);
            SyncRunResult result = await new SyncCoordinator().RunAsync(
                configuration,
                _configPath,
                stateRecoveryResolver: ConfirmStateRecoveryAsync,
                cancellationToken: _cancellation.Token);
            if (result.HasUnresolvedConflicts) {
                SetConflictCount(result.ConflictCount);
                SetStatus($"競合 {result.ConflictCount}件");
                if (configuration.Notifications.NotifyOnConflict) {
                    ShowNotification("競合があります", $"{result.ConflictCount}件の競合が保留されています。", ToolTipIcon.Warning);
                }
            } else {
                SetConflictCount(0);
                SetStatus($"同期済み {DateTime.Now:HH:mm:ss}");
                if (configuration.Notifications.NotifyOnSuccess && result.Files.Count > 0) {
                    ShowNotification("同期完了", $"コピー {result.CopiedCount}件、削除 {result.DeletedCount}件", ToolTipIcon.Info);
                }
            }
        } catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) {
        } catch (Exception exception) {
            SetStatus("エラー");
            if (_notifyOnError) {
                ShowNotification("同期エラー", exception.Message, ToolTipIcon.Error);
            }
        } finally {
            if (!_paused && !_cancellation.IsCancellationRequested) {
                _nextSyncAt = DateTime.Now.AddSeconds(_intervalSeconds);
            }
            _syncGate.Release();
        }
    }

    private void TogglePause() {
        _paused = !_paused;
        _pauseItem.Text = _paused ? "同期を再開" : "同期を一時停止";
        if (_paused) {
            _timer.Stop();
            _nextSyncAt = null;
            SetStatus("一時停止");
        } else {
            _timer.Start();
            _nextSyncAt = DateTime.Now;
            _ = SyncNowAsync();
        }
    }

    private async Task OpenConfigAsync() {
        if (!File.Exists(_configPath)) {
            SetStatus("初期設定が必要");
            await ShowSettingsAsync();
            return;
        }
        OpenPath(_configPath);
    }

    private async Task ShowSettingsAsync(bool syncAfterSave = true) {
        try {
            SyncConfiguration configuration = File.Exists(_configPath)
                ? await SyncConfiguration.LoadAsync(_configPath)
                : new SyncConfiguration();
            using SettingsForm form = new(_configPath, configuration);
            if (form.ShowDialog() == DialogResult.OK) {
                SyncConfiguration updated = await SyncConfiguration.LoadAsync(_configPath);
                StartupRegistration.SetEnabled(updated.StartWithWindows, _configPath);
                ShowNotification("設定を保存しました", "新しい設定で同期を開始します。", ToolTipIcon.Info);
                if (syncAfterSave) {
                    await SyncNowAsync();
                }
            }
        } catch (Exception exception) {
            ShowNotification("設定を開けません", exception.Message, ToolTipIcon.Error);
        }
    }

    private static Task<bool> ConfirmStateRecoveryAsync(StateCorruptionException exception, CancellationToken _) {
        DialogResult result = MessageBox.Show(
            $"同期状態ファイルが破損しています。{Environment.NewLine}{exception.StatePath}{Environment.NewLine}{Environment.NewLine}前世代のバックアップから復旧して同期を続けますか？{Environment.NewLine}破損したファイルは別名で保存されます。",
            "同期状態の復旧",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        return Task.FromResult(result == DialogResult.Yes);
    }

    private async Task ShowConflictsAsync() {
        while (!_cancellation.IsCancellationRequested) {
            SyncConfiguration configuration;
            PendingConflict[] conflicts;
            await _syncGate.WaitAsync(_cancellation.Token);
            try {
                configuration = await SyncConfiguration.LoadAsync(_configPath, _cancellation.Token);
                VaultSnapshot state = await new SyncStateStore().LoadAsync(
                    configuration.ResolveStatePath(_configPath),
                    configuration.LocalVaultPath,
                    configuration.IcloudVaultPath,
                    _cancellation.Token);
                conflicts = state.PendingConflicts.Values.ToArray();
                SetConflictCount(conflicts.Length);
            } catch (Exception exception) {
                ShowNotification("競合一覧を開けません", exception.Message, ToolTipIcon.Error);
                return;
            } finally {
                _syncGate.Release();
            }

            using ConflictListForm form = new(conflicts, configuration.LocalVaultPath, configuration.IcloudVaultPath);
            if (form.ShowDialog() != DialogResult.OK || form.ResolutionChoice is null) {
                return;
            }

            await _syncGate.WaitAsync(_cancellation.Token);
            try {
                ConflictResolutionResult result = await new ConflictResolutionService().ResolveAsync(
                    configuration,
                    _configPath,
                    form.SelectedConflicts,
                    form.ResolutionChoice.Value,
                    _cancellation.Token);
                if (result.RequiresReview) {
                    string paths = string.Join(Environment.NewLine, result.ChangedPaths.Concat(result.MissingPaths));
                    MessageBox.Show(
                        $"選択後に状態が変化したため、上書きせず中止しました。最新情報を確認してもう一度選択してください。{Environment.NewLine}{Environment.NewLine}{paths}",
                        "競合の再確認",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                } else {
                    ShowNotification("競合を解決しました", $"{result.AppliedCount}件を解決しました。", ToolTipIcon.Info);
                }
            } catch (Exception exception) {
                ShowNotification("競合を解決できません", exception.Message, ToolTipIcon.Error);
                return;
            } finally {
                _syncGate.Release();
            }
        }
    }

    private async Task ShowStatusHistoryAsync() {
        await _syncGate.WaitAsync(_cancellation.Token);
        IReadOnlyList<SyncHistoryEntry> history;
        try {
            SyncConfiguration configuration = await SyncConfiguration.LoadAsync(_configPath, _cancellation.Token);
            history = await new SyncHistoryStore().LoadAsync(
                configuration.ResolveStatePath(_configPath),
                _cancellation.Token);
        } catch (Exception exception) {
            ShowNotification("同期履歴を開けません", exception.Message, ToolTipIcon.Error);
            return;
        } finally {
            _syncGate.Release();
        }

        using StatusHistoryForm form = new(_currentStatus, _nextSyncAt, history);
        form.ShowDialog();
    }

    private async Task ShowBackupsAsync() {
        _timer.Stop();
        try {
            while (!_cancellation.IsCancellationRequested) {
                SyncConfiguration configuration;
                IReadOnlyList<BackupEntry> entries;
                await _syncGate.WaitAsync(_cancellation.Token);
                try {
                    configuration = await SyncConfiguration.LoadAsync(_configPath, _cancellation.Token);
                    entries = new BackupManager().List(configuration.ResolveStatePath(_configPath));
                } catch (Exception exception) {
                    ShowNotification("バックアップ一覧を開けません", exception.Message, ToolTipIcon.Error);
                    return;
                } finally {
                    _syncGate.Release();
                }

                using BackupListForm form = new(entries);
                if (form.ShowDialog() != DialogResult.OK || form.SelectedEntry is null) {
                    return;
                }

                BackupEntry selected = form.SelectedEntry;
                BackupRestoreService service = new();
                bool destinationExists;
                try {
                    destinationExists = service.DestinationExists(selected, configuration);
                } catch (Exception exception) {
                    ShowNotification("復元先を確認できません", exception.Message, ToolTipIcon.Error);
                    return;
                }
                string destination = $"{selected.Side}/{selected.RelativePath}";
                string prompt = destinationExists
                    ? $"復元先 {destination} は既に存在します。現在のファイルを上書きして復元しますか？"
                    : $"{destination} へバックアップを復元しますか？";
                if (MessageBox.Show(prompt, "バックアップ復元", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) {
                    continue;
                }

                await _syncGate.WaitAsync(_cancellation.Token);
                try {
                    await service.RestoreAsync(
                        selected,
                        configuration,
                        _configPath,
                        destinationExists,
                        _cancellation.Token);
                    SetStatus($"復元済み {DateTime.Now:HH:mm:ss}");
                    ShowNotification("バックアップを復元しました", destination, ToolTipIcon.Info);
                } catch (Exception exception) {
                    ShowNotification("バックアップを復元できません", exception.Message, ToolTipIcon.Error);
                    return;
                } finally {
                    _syncGate.Release();
                }
            }
        } finally {
            if (!_paused && !_cancellation.IsCancellationRequested) {
                _timer.Start();
                _nextSyncAt = DateTime.Now.AddSeconds(_intervalSeconds);
            }
        }
    }

    private void OpenLogs() {
        string statePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_configPath))!, "state.json");
        try {
            if (File.Exists(_configPath)) {
                statePath = SyncConfiguration.LoadAsync(_configPath).GetAwaiter().GetResult().ResolveStatePath(_configPath);
            }
            string directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(statePath))!, "logs");
            Directory.CreateDirectory(directory);
            OpenPath(directory);
        } catch (Exception exception) {
            ShowNotification("ログを開けません", exception.Message, ToolTipIcon.Error);
        }
    }

    private static void OpenPath(string path) {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void SetStatus(string status) {
        _currentStatus = status;
        string text = $"ObsidianWinSync - {status}";
        _icon.Text = text[..Math.Min(63, text.Length)];
    }

    private void SetConflictCount(int count) =>
        _conflictsItem.Text = count == 0 ? "競合を確認" : $"競合を確認 ({count})";

    private void ShowNotification(string title, string text, ToolTipIcon icon) {
        string key = $"{icon}\n{title}\n{text}";
        if (!_notificationThrottle.ShouldShow(key, TimeSpan.FromSeconds(_notificationMinimumIntervalSeconds))) {
            return;
        }
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.BalloonTipIcon = icon;
        _icon.ShowBalloonTip(5000);
    }

    private async Task ExitAsync() {
        _timer.Stop();
        _cancellation.Cancel();
        await _syncGate.WaitAsync();
        _syncGate.Release();
        _icon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            _timer.Dispose();
            _icon.Dispose();
            _cancellation.Dispose();
            _syncGate.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal static class StartupRegistration {
    private const string Name = "ObsidianWinSync";

    public static void SetEnabled(bool enabled, string configPath) {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (!enabled) {
            key.DeleteValue(Name, false);
            return;
        }

        key.SetValue(Name, $"\"{Application.ExecutablePath}\" --config \"{Path.GetFullPath(configPath)}\"");
    }
}
