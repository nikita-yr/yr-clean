using YrClean.Core.Models;

namespace YrClean.Core.Services;

public static class AutoCleanRunner
{
    // The unattended (--auto-clean) run: quarantines everything old enough in the enabled Safe
    // rules. Review and Expert rules, including auto-discovered folders, are manual-only.
    public static DeleteResult Run(CleanSettings settings)
    {
        QuarantineService.Default.Purge(settings.QuarantineDays);

        var rules = RuleEngine.LoadRelevant(settings)
            .Where(s => s.Level == RuleLevel.Safe && !settings.DisabledSourceNames.Contains(s.Name));
        var scans = SourceScanner.Scan(rules, settings.ExcludedPaths).Where(s => !s.Busy).ToList();

        var result = SafeDeleteService.DeleteFiles(
            scans.SelectMany(s => s.Files).ToList(),
            scans.SelectMany(s => s.Roots).ToList(),
            settings.MinAgeDays,
            settings.ExcludedPaths,
            minAgeOverrides: SourceScanner.MinAgeOverrides(scans));
        CleanupLog.Write("scheduled", result);
        return result;
    }
}
