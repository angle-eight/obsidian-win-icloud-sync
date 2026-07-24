using System.Text;

namespace ObsidianWinSync.Sync;

public sealed record ConflictDetail(bool IsTextDiff, string Text);

public static class ConflictDetailBuilder {
    private const long MaximumTextBytes = 1_000_000;
    private const int MaximumRenderedLines = 2_000;
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".md", ".txt", ".json", ".yaml", ".yml", ".css", ".js", ".ts", ".html", ".xml", ".csv"
    };

    public static ConflictDetail Build(PendingConflict conflict, string localRoot, string icloudRoot) {
        string localPath = SafePath(localRoot, conflict.RelativePath);
        string cloudPath = SafePath(icloudRoot, conflict.RelativePath);
        if (CanShowTextDiff(conflict.RelativePath, localPath, cloudPath)) {
            try {
                string localText = ReadUtf8(localPath);
                string cloudText = ReadUtf8(cloudPath);
                return new ConflictDetail(true, BuildTextDiff(localText, cloudText));
            } catch (DecoderFallbackException) {
                // A text extension can still contain binary data; show safe metadata instead.
            }
        }

        StringBuilder details = new();
        details.AppendLine($"ファイル: {conflict.RelativePath}");
        details.AppendLine("テキスト差分を表示できないため、保存時点のメタデータを表示します。");
        details.AppendLine();
        AppendMetadata(details, "local", conflict.Local);
        details.AppendLine();
        AppendMetadata(details, "iCloud", conflict.Cloud);
        return new ConflictDetail(false, details.ToString());
    }

    private static bool CanShowTextDiff(string relativePath, string localPath, string cloudPath) =>
        TextExtensions.Contains(Path.GetExtension(relativePath))
        && File.Exists(localPath)
        && File.Exists(cloudPath)
        && new FileInfo(localPath).Length <= MaximumTextBytes
        && new FileInfo(cloudPath).Length <= MaximumTextBytes;

    private static string ReadUtf8(string path) {
        byte[] bytes = File.ReadAllBytes(path);
        int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
    }

    private static string BuildTextDiff(string localText, string cloudText) {
        string[] local = SplitLines(localText);
        string[] cloud = SplitLines(cloudText);
        IReadOnlyList<DiffLine> lines = (long)local.Length * cloud.Length <= 1_000_000
            ? BuildLcsDiff(local, cloud)
            : BuildPositionalDiff(local, cloud);
        StringBuilder output = new();
        output.AppendLine("       local |      iCloud | 内容");
        output.AppendLine("-------------+-------------+----------------------------------------");
        foreach (DiffLine line in lines.Take(MaximumRenderedLines)) {
            string localNumber = line.LocalLine?.ToString() ?? "";
            string cloudNumber = line.CloudLine?.ToString() ?? "";
            output.AppendLine($"{line.Marker} {localNumber,9} | {cloudNumber,11} | {line.Text}");
        }
        if (lines.Count > MaximumRenderedLines) {
            output.AppendLine($"... 差分表示は先頭{MaximumRenderedLines:N0}行までです");
        }
        return output.ToString();
    }

    private static IReadOnlyList<DiffLine> BuildLcsDiff(string[] local, string[] cloud) {
        int[,] lengths = new int[local.Length + 1, cloud.Length + 1];
        for (int left = local.Length - 1; left >= 0; left--) {
            for (int right = cloud.Length - 1; right >= 0; right--) {
                lengths[left, right] = string.Equals(local[left], cloud[right], StringComparison.Ordinal)
                    ? lengths[left + 1, right + 1] + 1
                    : Math.Max(lengths[left + 1, right], lengths[left, right + 1]);
            }
        }

        List<DiffLine> result = [];
        int localIndex = 0;
        int cloudIndex = 0;
        while (localIndex < local.Length || cloudIndex < cloud.Length) {
            if (localIndex < local.Length && cloudIndex < cloud.Length
                && string.Equals(local[localIndex], cloud[cloudIndex], StringComparison.Ordinal)) {
                result.Add(new DiffLine(' ', localIndex + 1, cloudIndex + 1, local[localIndex]));
                localIndex++;
                cloudIndex++;
            } else if (cloudIndex < cloud.Length
                       && (localIndex == local.Length || lengths[localIndex, cloudIndex + 1] >= lengths[localIndex + 1, cloudIndex])) {
                result.Add(new DiffLine('+', null, cloudIndex + 1, cloud[cloudIndex++]));
            } else {
                result.Add(new DiffLine('-', localIndex + 1, null, local[localIndex++]));
            }
        }
        return result;
    }

    private static IReadOnlyList<DiffLine> BuildPositionalDiff(string[] local, string[] cloud) {
        List<DiffLine> result = [];
        for (int index = 0; index < Math.Max(local.Length, cloud.Length); index++) {
            string? left = index < local.Length ? local[index] : null;
            string? right = index < cloud.Length ? cloud[index] : null;
            if (left is not null && right is not null && string.Equals(left, right, StringComparison.Ordinal)) {
                result.Add(new DiffLine(' ', index + 1, index + 1, left));
            } else {
                if (left is not null) {
                    result.Add(new DiffLine('-', index + 1, null, left));
                }
                if (right is not null) {
                    result.Add(new DiffLine('+', null, index + 1, right));
                }
            }
        }
        return result;
    }

    private static string[] SplitLines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static void AppendMetadata(StringBuilder output, string side, FileFingerprint? fingerprint) {
        output.AppendLine($"[{side}]");
        if (fingerprint is null) {
            output.AppendLine("状態: 削除済み");
            return;
        }
        output.AppendLine($"サイズ: {fingerprint.Length:N0} bytes");
        output.AppendLine($"更新日時: {fingerprint.LastWriteTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        output.AppendLine($"SHA-256: {fingerprint.Hash}");
    }

    private static string SafePath(string root, string relativePath) {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string candidate = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!candidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidDataException($"Vault外のパスは表示できません: {relativePath}");
        }
        return candidate;
    }

    private sealed record DiffLine(char Marker, int? LocalLine, int? CloudLine, string Text);
}
