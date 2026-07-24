using ObsidianWinSync.Sync;

namespace ObsidianWinSync.Tray;

internal sealed class ConflictListForm : Form {
    private readonly ListView _list;

    public IReadOnlyList<PendingConflict> SelectedConflicts { get; private set; } = [];
    public ConflictChoice? ResolutionChoice { get; private set; }

    public ConflictListForm(IEnumerable<PendingConflict> conflicts, string localRoot, string icloudRoot) {
        Text = "ObsidianWinSync - 競合一覧";
        Width = 1050;
        Height = 520;
        MinimumSize = new Size(760, 360);
        StartPosition = FormStartPosition.CenterScreen;

        _list = new ListView {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            GridLines = true,
            HideSelection = false
        };
        _list.Columns.Add("相対パス", 260);
        _list.Columns.Add("競合種別", 110);
        _list.Columns.Add("初回検出", 145);
        _list.Columns.Add("local 更新日時", 145);
        _list.Columns.Add("local サイズ", 95, HorizontalAlignment.Right);
        _list.Columns.Add("iCloud 更新日時", 145);
        _list.Columns.Add("iCloud サイズ", 95, HorizontalAlignment.Right);

        ConflictListItem[] items = conflicts
            .Select(ConflictListItem.From)
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Dictionary<string, PendingConflict> pendingByPath = conflicts.ToDictionary(
            conflict => conflict.RelativePath,
            StringComparer.OrdinalIgnoreCase);
        foreach (ConflictListItem conflict in items) {
            ListViewItem row = new(conflict.RelativePath);
            row.Tag = pendingByPath[conflict.RelativePath];
            row.SubItems.Add(conflict.Kind);
            row.SubItems.Add(FormatTime(conflict.DetectedAtUtc));
            row.SubItems.Add(FormatTime(conflict.LocalModifiedAtUtc));
            row.SubItems.Add(FormatLength(conflict.LocalLength));
            row.SubItems.Add(FormatTime(conflict.IcloudModifiedAtUtc));
            row.SubItems.Add(FormatLength(conflict.IcloudLength));
            _list.Items.Add(row);
        }

        RichTextBox details = new() {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9),
            Text = items.Length == 0 ? "競合はありません。" : "一覧から競合を選択すると詳細を表示します。"
        };
        _list.SelectedIndexChanged += (_, _) => {
            if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not PendingConflict selected) {
                return;
            }
            try {
                details.Text = ConflictDetailBuilder.Build(selected, localRoot, icloudRoot).Text;
                details.SelectionStart = 0;
                details.ScrollToCaret();
            } catch (Exception exception) {
                details.Text = $"詳細を読み取れません。{Environment.NewLine}{exception.Message}";
            }
        };

        SplitContainer split = new() {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 225,
            Panel1MinSize = 120,
            Panel2MinSize = 100
        };
        split.Panel1.Controls.Add(_list);
        split.Panel2.Controls.Add(details);

        Label summary = new() {
            Dock = DockStyle.Top,
            Height = 38,
            Padding = new Padding(10),
            Text = items.Length == 0
                ? "保留中の競合はありません。"
                : $"保留中の競合: {items.Length}件（項目を選択すると差分またはメタデータを表示します）"
        };
        Button close = new() { Text = "閉じる", AutoSize = true, DialogResult = DialogResult.Cancel };
        Button useLocal = new() { Text = "選択項目でlocalを採用", AutoSize = true, Enabled = items.Length > 0 };
        Button useIcloud = new() { Text = "選択項目でiCloudを採用", AutoSize = true, Enabled = items.Length > 0 };
        useLocal.Click += (_, _) => RequestResolution(ConflictChoice.Local);
        useIcloud.Click += (_, _) => RequestResolution(ConflictChoice.Icloud);
        FlowLayoutPanel buttons = new() {
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(8),
            FlowDirection = FlowDirection.RightToLeft
        };
        buttons.Controls.Add(close);
        buttons.Controls.Add(useIcloud);
        buttons.Controls.Add(useLocal);

        Controls.Add(split);
        Controls.Add(summary);
        Controls.Add(buttons);
        CancelButton = close;
    }

    private void RequestResolution(ConflictChoice choice) {
        PendingConflict[] selected = _list.SelectedItems
            .Cast<ListViewItem>()
            .Select(item => item.Tag)
            .OfType<PendingConflict>()
            .ToArray();
        if (selected.Length == 0) {
            MessageBox.Show("解決する競合を1件以上選択してください。", "競合の解決", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string side = choice == ConflictChoice.Local ? "local" : "iCloud";
        DialogResult confirmation = MessageBox.Show(
            $"選択した{selected.Length}件で{side}側を採用します。上書き・削除前にはバックアップを作成します。続けますか？",
            "競合の解決",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirmation != DialogResult.Yes) {
            return;
        }
        SelectedConflicts = selected;
        ResolutionChoice = choice;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string FormatTime(DateTime? value) =>
        value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "削除済み";

    private static string FormatLength(long? value) =>
        value is null ? "-" : $"{value:N0} bytes";
}
