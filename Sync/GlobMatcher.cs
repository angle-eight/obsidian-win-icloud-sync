using System.Text.RegularExpressions;

namespace ObsidianWinSync.Sync;

public sealed class GlobMatcher {
    private readonly Regex[] _patterns;

    public GlobMatcher(IEnumerable<string> patterns) {
        _patterns = patterns
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(ToRegex)
            .ToArray();
    }

    public bool IsMatch(string relativePath) {
        string normalized = relativePath.Replace('\\', '/');
        return _patterns.Any(pattern => pattern.IsMatch(normalized));
    }

    private static Regex ToRegex(string glob) {
        string normalized = glob.Replace('\\', '/');
        string expression = Regex.Escape(normalized)
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", "[^/]");
        return new Regex($"^{expression}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
