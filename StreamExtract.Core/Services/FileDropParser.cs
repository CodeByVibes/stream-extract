namespace StreamExtract.Services;

public static class FileDropParser
{
    private static readonly HashSet<string> IgnoredTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "copy", "cut", "paste"
    };

    public static IReadOnlyList<string> ParseUriList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];

        var paths = new List<string>();
        var seen = new HashSet<string>(PathComparer.Default);
        foreach (var line in value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var item = line.Trim().Trim('"', '\'');
            if (item.Length == 0 || item.StartsWith('#')) continue;
            if (item.StartsWith("x-special/", StringComparison.OrdinalIgnoreCase)) continue;
            if (IgnoredTokens.Contains(item)) continue;

            string? path = null;
            if (Uri.TryCreate(item, UriKind.Absolute, out var uri))
            {
                if (!uri.IsFile) continue;
                path = uri.LocalPath;
            }
            else if (Path.IsPathRooted(item) || item.Contains(Path.DirectorySeparatorChar) || item.Contains('/'))
            {
                path = item;
            }

            if (!string.IsNullOrWhiteSpace(path) && seen.Add(path))
                paths.Add(path);
        }

        return paths;
    }
}
