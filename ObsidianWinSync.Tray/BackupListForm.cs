using ObsidianWinSync.Sync;

namespace ObsidianWinSync.Tray;

internal sealed class BackupListForm : Form {
    private readonly ListView _list;

    public BackupEntry? SelectedEntry { get; private set; }

    public BackupListForm(IReadOnlyList<BackupEntry> entries) {
        Text = "ObsidianWinSync - バックアップ復元";
        Width = 900;
        Height = 460;
        MinimumSize = new Size(680, 330);
        StartPosition = FormStartPosition.CenterScreen;

        Label summary = new() {
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(10),
            Text = entries.Count == 0
                ? "復元可能なバックアップはありません。"
                : $"復元可能なバックアップ: {entries.Count}件"
        };
        _list = new ListView {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = true,
            HideSelection = false
        };
        _list.Columns.Add("作成時刻", 145);
        _list.Columns.Add("実行ID", 205);
        _list.Columns.Add("復元先", 75);
        _list.Columns.Add("相対パス", 330);
        _list.Columns.Add("サイズ", 105, HorizontalAlignment.Right);
        foreach (BackupEntry entry in entries) {
            BackupListItem item = BackupListItem.From(entry);
            ListViewItem row = new(item.CreatedAtLocal.ToString("yyyy-MM-dd HH:mm:ss")) { Tag = entry };
            row.SubItems.Add(item.RunId);
            row.SubItems.Add(item.Side);
            row.SubItems.Add(item.RelativePath);
            row.SubItems.Add($"{item.Length:N0} bytes");
            _list.Items.Add(row);
        }

        Button close = new() { Text = "閉じる", AutoSize = true, DialogResult = DialogResult.Cancel };
        Button restore = new() { Text = "選択したバックアップを復元", AutoSize = true, Enabled = entries.Count > 0 };
        restore.Click += (_, _) => RequestRestore();
        _list.DoubleClick += (_, _) => RequestRestore();
        FlowLayoutPanel buttons = new() {
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(8),
            FlowDirection = FlowDirection.RightToLeft
        };
        buttons.Controls.Add(close);
        buttons.Controls.Add(restore);
        Controls.Add(_list);
        Controls.Add(summary);
        Controls.Add(buttons);
        CancelButton = close;
    }

    private void RequestRestore() {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not BackupEntry entry) {
            MessageBox.Show("復元するバックアップを選択してください。", "バックアップ復元", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        SelectedEntry = entry;
        DialogResult = DialogResult.OK;
        Close();
    }
}
