using YrClean.Core.Models;

namespace YrClean.Core.Services;

public static class AutoCleanRunner
{
    // Scans every enabled source and deletes everything old enough, per the saved settings.
    // Used by the --auto-clean silent run, and later reusable by a "clean all" UI button.
    public static DeleteResult Run(CleanSettings settings)
    {
        var scanner = new FolderScanner();
        IEnumerable<CacheSource> sources = CacheSourceProvider.GetKnownSources();

        if (settings.IncludeAutoDiscoveredInScheduledRun)
            sources = sources.Concat(AutoDiscoveryScanner.Discover());

        var roots = sources
            .Where(s => !settings.DisabledSourceNames.Contains(s.Name))
            .SelectMany(s => s.ResolvePaths(settings.ExcludedPaths))
            .ToList();

        var allFiles = roots
            .SelectMany(root => scanner.Scan(root))
            .Select(f => f.FullPath)
            .ToList();

        var result = SafeDeleteService.DeleteFiles(allFiles, roots, settings.MinAgeDays);
        CleanupLog.Write("scheduled", result);
        return result;
    }
}
