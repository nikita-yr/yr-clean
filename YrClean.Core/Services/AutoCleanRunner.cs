using YrClean.Core.Models;

namespace YrClean.Core.Services;

public static class AutoCleanRunner
{
    // The unattended (--auto-clean) run: quarantines everything old enough in the enabled Safe
    // sources. Review and Expert sources, including auto-discovered folders, are manual-only.
    public static DeleteResult Run(CleanSettings settings)
    {
        QuarantineService.Default.Purge(settings.QuarantineDays);

        var scanner = new FolderScanner();
        var roots = CacheSourceProvider.GetKnownSources()
            .Where(s => s.Level == RuleLevel.Safe && !settings.DisabledSourceNames.Contains(s.Name))
            .SelectMany(s => s.ResolvePaths(settings.ExcludedPaths))
            .ToList();

        var allFiles = roots
            .SelectMany(root => scanner.Scan(root, settings.ExcludedPaths))
            .Select(f => f.FullPath)
            .ToList();

        var result = SafeDeleteService.DeleteFiles(allFiles, roots, settings.MinAgeDays, settings.ExcludedPaths);
        CleanupLog.Write("scheduled", result);
        return result;
    }
}
