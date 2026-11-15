using YrClean.Core.Models;

namespace YrClean.Core.Services;

public static class AutoDiscoveryScanner
{
    private const int MaxDepth = 4;
    private const int MaxFoldersVisited = 20_000;

    public static List<CacheSource> Discover()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetEnvironmentVariable("ProgramData")
        }.Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)).Cast<string>();

        var found = new List<string>();
        var visited = 0;

        foreach (var root in roots)
            Walk(root, 0, found, ref visited);

        return found.Distinct().Select(ToSource).ToList();
    }

    private static void Walk(string path, int depth, List<string> results, ref int visited)
    {
        if (depth > MaxDepth || visited >= MaxFoldersVisited) return;

        IEnumerable<string> subdirs;
        try
        {
            subdirs = Directory.EnumerateDirectories(path);
        }
        catch (Exception)
        {
            return;
        }

        foreach (var dir in subdirs)
        {
            if (visited++ >= MaxFoldersVisited) return;

            try
            {
                if (new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint))
                    continue;
            }
            catch (Exception)
            {
                continue;
            }

            if (CacheHeuristics.IsExcluded(dir) || ProtectedLocations.IsForbidden(dir)) continue;

            var name = Path.GetFileName(dir);
            if (CacheHeuristics.LooksLikeCacheFolder(name))
            {
                results.Add(dir);
                continue;
            }

            Walk(dir, depth + 1, results, ref visited);
        }
    }

    private static CacheSource ToSource(string fullPath)
    {
        var parts = fullPath.Split(Path.DirectorySeparatorChar);
        var label = parts.Length >= 2 ? $"{parts[^2]} — {parts[^1]}" : parts[^1];

        return new CacheSource
        {
            Name = label,
            Id = "auto:" + fullPath,
            Paths = { new RulePath { Path = fullPath } },
            Category = "Auto-discovered",
            Level = RuleLevel.Review // a name heuristic is a guess, so a person must look first
        };
    }
}