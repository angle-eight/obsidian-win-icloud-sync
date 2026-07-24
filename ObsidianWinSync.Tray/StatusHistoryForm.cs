using ObsidianWinSync.Sync;

namespace ObsidianWinSync.Tray;

internal sealed class StatusHistoryForm : Form {
    public StatusHistoryForm(string currentStatus, DateTime? nextSyncAt, IReadOnlyList<SyncHistoryEntry> history) {
        Text = "ObsidianWinSync - 同期状態と履歴";
        Width = 900;
        Height = 480;
        MinimumSize = new Size(680, 340);
        StartPosition = FormStartPosition.CenterScreen;

        SyncHistoryListItem[] items = history.Select(SyncHistoryListItem.From).ToArray();
        SyncHistoryListItem? latest = items.FirstOrDefault();
        Label summary = new() {
            Dock = DockStyle.Top,
            Height = 70,
            Padding = new Padding(10),
            Text = $"現在: {currentStatus}{Environment.NewLine}"
                + $"次回同期: {(nextSyncAt is null ? "予定なし" : nextSyncAt.Value.ToString("yyyy-MM-dd HH:mm:ss"))}"
                + (latest is null ? "" : $"　最終結果: {latest.Status}（{latest.Counts}）")
        };

        ListView list = new() {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true
        };
        list.Columns.Add("開始時刻", 145);
        list.Columns.Add("結果", 95);
        list.Columns.Add("件数", 235);
        list.Columns.Add("所要時間", 85, HorizontalAlignment.Right);
        list.Columns.Add("エラー", 290);
        foreach (SyncHistoryListItem item in items) {
            ListViewItem row = new(item.StartedAtLocal.ToString("yyyy-MM-dd HH:mm:ss"));
            row.SubItems.Add(item.Status);
            row.SubItems.Add(item.Counts);
            row.SubItems.Add($"{item.Duration.TotalSeconds:F1}秒");
            row.SubItems.Add(item.Error);
            list.Items.Add(row);
        }

        Button close = new() { Text = "閉じる", AutoSize = true, DialogResult = DialogResult.OK };
        FlowLayoutPanel buttons = new() {
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(8),
            FlowDirection = FlowDirection.RightToLeft
        };
        buttons.Controls.Add(close);
        Controls.Add(list);
        Controls.Add(summary);
        Controls.Add(buttons);
        AcceptButton = close;
        CancelButton = close;
    }
}
