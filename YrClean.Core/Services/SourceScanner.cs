using YrClean.Core.Models;

namespace YrClean.Core.Services;

// What one rule found on disk. Busy means its program is running, so nothing was scanned.
public record SourceScan(CacheSource Source, List<string> Roots, List<CacheGroup> Groups, bool Busy)
{
    public long TotalBytes => Groups.Sum(g => g.TotalSizeBytes);
    public IEnumerable<string> Files => Groups.SelectMany(g => g.Files).Select(f => f.FullPath);
}

// Turns rules into scan results; shared by the main window and the unattended run.
public static class SourceScanner
{
    public static List<SourceScan> Scan(IEnumerable<CacheSource> sources, IReadOnlyCollection<string> excludedPaths)
    {
        var scanner = new FolderScanner();
        var scans = new List<SourceScan>();
        var claimedRoots = new List<string>();

        foreach (var source in sources)
        {
            // A folder belongs to the first rule that names it; later rules (and auto-discovery)
            // do not get to list the same files again, e.g. to get around a "busy" skip.
            var roots = source.Resolve(excludedPaths)
                .Where(root => !claimedRoots.Any(claimed =>
                    ProtectedLocations.IsUnder(root.Path, claimed) || ProtectedLocations.IsUnder(claimed, root.Path)))
                .ToList();
            if (roots.Count == 0)
                continue;

            var rootPaths = roots.Select(r => r.Path).ToList();
            claimedRoots.AddRange(rootPaths);

            if (source.IsBusy())
            {
                scans.Add(new SourceScan(source, rootPaths, new List<CacheGroup>(), Busy: true));
                continue;
            }

            var excluded = excludedPaths.Concat(source.ExpandedExcludes).ToList();
            var groups = roots
                .SelectMany(root => scanner.ScanGrouped(root.Path, excluded, root.Spec.Masks, root.Spec.Recurse))
                .ToList();

            if (groups.Sum(g => g.TotalSizeBytes) > 0)
                scans.Add(new SourceScan(source, rootPaths, groups, Busy: false));
        }

        return scans;
    }

    // Roots whose rule asks for a longer minimum age than the global setting.
    public static Dictionary<string, int> MinAgeOverrides(IEnumerable<SourceScan> scans)
    {
        var overrides = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var scan in scans.Where(s => s.Source.MinAgeDays > 0))
        {
            foreach (var root in scan.Roots)
                overrides[root] = scan.Source.MinAgeDays;
        }

        return overrides;
    }
}
