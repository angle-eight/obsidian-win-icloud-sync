using System.Text.Json;
using System.Text.Json.Nodes;
using ObsidianWinSync.Configuration;

namespace ObsidianWinSync.Tray;

internal sealed class SettingsForm : Form {
    private readonly string _configPath;
    private readonly TextBox _localPath = new() { Dock = DockStyle.Fill };
    private readonly TextBox _cloudPath = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _interval = new() { Minimum = 1, Maximum = int.MaxValue / 1000, Value = 30 };
    private readonly CheckBox _backupEnabled = new() { Text = "バックアップを有効にする", AutoSize = true };
    private readonly NumericUpDown _backupDays = new() { Minimum = 0, Maximum = 3650, Value = 7 };
    private readonly NumericUpDown _backupSize = new() { Minimum = 1, Maximum = 1024 * 1024, Value = 1024 };
    private readonly CheckBox _startWithWindows = new() { Text = "Windows ログイン時に起動", AutoSize = true };
    private readonly CheckBox _notifyOnSuccess = new() { Text = "同期成功を通知", AutoSize = true };
    private readonly NumericUpDown _notificationInterval = new() { Minimum = 0, Maximum = int.MaxValue / 1000, Value = 300 };

    public SettingsForm(string configPath, SyncConfiguration configuration) {
        _configPath = configPath;
        Text = "ObsidianWinSync 設定";
        Width = 720;
        Height = 370;
        MinimumSize = new Size(600, 300);
        StartPosition = FormStartPosition.CenterScreen;

        _localPath.Text = configuration.LocalVaultPath;
        _cloudPath.Text = configuration.IcloudVaultPath;
        _interval.Value = Math.Clamp(configuration.IntervalSeconds, 1, int.MaxValue / 1000);
        _backupEnabled.Checked = configuration.Backup.Enabled;
        _backupDays.Value = Math.Clamp(configuration.Backup.RetentionDays, 0, 3650);
        _backupSize.Value = Math.Clamp(configuration.Backup.MaximumSizeMb, 1, 1024 * 1024);
        _startWithWindows.Checked = configuration.StartWithWindows;
        _notifyOnSuccess.Checked = configuration.Notifications.NotifyOnSuccess;
        _notificationInterval.Value = Math.Clamp(configuration.Notifications.MinimumIntervalSeconds, 0, int.MaxValue / 1000);

        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 3, RowCount = 10 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddPathRow(layout, 0, "ローカル Vault", _localPath);
        AddPathRow(layout, 1, "iCloud Vault", _cloudPath);
        AddRow(layout, 2, "同期間隔（秒）", _interval);
        layout.Controls.Add(_backupEnabled, 1, 3);
        AddRow(layout, 4, "バックアップ保持日数", _backupDays);
        AddRow(layout, 5, "バックアップ上限（MB）", _backupSize);
        layout.Controls.Add(_startWithWindows, 1, 6);
        layout.Controls.Add(_notifyOnSuccess, 1, 7);
        AddRow(layout, 8, "同一通知の抑制（秒）", _notificationInterval);

        FlowLayoutPanel buttons = new() { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        Button save = new() { Text = "保存", AutoSize = true };
        Button cancel = new() { Text = "キャンセル", AutoSize = true, DialogResult = DialogResult.Cancel };
        save.Click += async (_, _) => await SaveAsync();
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 1, 9);
        Controls.Add(layout);
        CancelButton = cancel;
    }

    private static void AddPathRow(TableLayoutPanel layout, int row, string label, TextBox textBox) {
        AddRow(layout, row, label, textBox);
        Button browse = new() { Text = "参照...", AutoSize = true };
        browse.Click += (_, _) => {
            using FolderBrowserDialog dialog = new() { SelectedPath = textBox.Text, ShowNewFolderButton = true };
            if (dialog.ShowDialog() == DialogResult.OK) {
                textBox.Text = dialog.SelectedPath;
            }
        };
        layout.Controls.Add(browse, 2, row);
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control) {
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    private async Task SaveAsync() {
        SyncConfiguration candidate = new() {
            LocalVaultPath = _localPath.Text.Trim(),
            IcloudVaultPath = _cloudPath.Text.Trim(),
            IntervalSeconds = decimal.ToInt32(_interval.Value),
            Backup = new BackupConfiguration {
                Enabled = _backupEnabled.Checked,
                RetentionDays = decimal.ToInt32(_backupDays.Value),
                MaximumSizeMb = decimal.ToInt32(_backupSize.Value)
            }
        };
        IReadOnlyList<string> errors = candidate.Validate();
        if (errors.Count > 0) {
            MessageBox.Show(string.Join(Environment.NewLine, errors), "設定エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        JsonObject root = File.Exists(_configPath)
            ? JsonNode.Parse(await File.ReadAllTextAsync(_configPath))?.AsObject() ?? new JsonObject()
            : new JsonObject();
        root["localVaultPath"] = candidate.LocalVaultPath;
        root["icloudVaultPath"] = candidate.IcloudVaultPath;
        root["intervalSeconds"] = candidate.IntervalSeconds;
        root["startWithWindows"] = _startWithWindows.Checked;
        JsonObject backup = root["backup"] as JsonObject ?? new JsonObject();
        backup["enabled"] = candidate.Backup.Enabled;
        backup["retentionDays"] = candidate.Backup.RetentionDays;
        backup["maximumSizeMb"] = candidate.Backup.MaximumSizeMb;
        root["backup"] = backup;
        JsonObject notifications = root["notifications"] as JsonObject ?? new JsonObject();
        notifications["notifyOnSuccess"] = _notifyOnSuccess.Checked;
        notifications["notifyOnConflict"] ??= true;
        notifications["notifyOnError"] ??= true;
        notifications["minimumIntervalSeconds"] = decimal.ToInt32(_notificationInterval.Value);
        root["notifications"] = notifications;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_configPath))!);
        string temporary = _configPath + ".tmp";
        await File.WriteAllTextAsync(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _configPath, true);
        DialogResult = DialogResult.OK;
        Close();
    }
}
