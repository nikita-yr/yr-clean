using YrClean.Core.Models;

namespace YrClean.Core.Services;

public static class SafeDeleteService
{
    // File types that must never be deleted, even if they somehow end up selected
    // inside a cache folder — protects against breaking the system or an app.
    private static readonly HashSet<string> ProtectedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".sys", ".msi", ".ocx", ".drv", ".lnk", ".ini", ".bat", ".cmd", ".ps1", ".vbs"
    };

    private enum SafetyCheck
    {
        Passed,
        OutsideRoot,
        ProtectedExtension,
        Missing,
        ReparsePoint,
        TooRecent
    }

    private enum DenyReason
    {
        AccessDenied,
        InUse
    }

    public static ClassificationResult Classify(
        IEnumerable<string> filePaths,
        IEnumerable<string> allowedRoots,
        int minAgeDays)
    {
        var result = new ClassificationResult();
        var policy = new SafetyPolicy(allowedRoots, minAgeDays);

        foreach (var path in filePaths)
        {
            try
            {
                var info = new FileInfo(Path.GetFullPath(path));
                var check = policy.Check(info);
                if (check != SafetyCheck.Passed)
                {
                    RecordSkip(result, check);
                    continue;
                }

                if (CanLikelyDelete(info, out var reason))
                {
                    result.DeletableNow.Add(info.FullName);
                    result.DeletableNowBytes += info.Length;
                }
                else if (reason == DenyReason.AccessDenied)
                {
                    result.NeedsAdmin.Add(info.FullName);
                    result.NeedsAdminBytes += info.Length;
                }
                else
                {
                    result.SkippedInUseCount++;
                }
            }
            catch (Exception)
            {
                result.SkippedInUseCount++;
            }
        }

        return result;
    }

    public static DeleteResult DeleteFiles(
        IEnumerable<string> filePaths,
        IEnumerable<string> allowedRoots,
        int minAgeDays)
    {
        var result = new DeleteResult();
        var policy = new SafetyPolicy(allowedRoots, minAgeDays);

        foreach (var path in filePaths)
        {
            try
            {
                var info = new FileInfo(Path.GetFullPath(path));
                var check = policy.Check(info);
                if (check != SafetyCheck.Passed)
                {
                    RecordSkip(result, check);
                    if (BlockedMessage(check) is { } message)
                        result.Errors.Add($"{message}: {info.FullName}");
                    continue;
                }

                long size = info.Length;

                // Like `del /f`: the read-only flag makes Delete throw "access denied" even for an
                // administrator. The file has already passed every safety check above.
                if (info.IsReadOnly)
                    info.IsReadOnly = false;

                info.Delete();
                result.DeletedCount++;
                result.FreedBytes += size;
            }
            catch (UnauthorizedAccessException ex)
            {
                result.Errors.Add($"Access denied (needs administrator): {path} - {ex.Message}");
                result.SkippedAccessDeniedCount++;
            }
            catch (IOException ex)
            {
                result.Errors.Add($"In use by another process: {path} - {ex.Message}");
                result.SkippedInUseCount++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{path}: {ex.Message}");
                result.SkippedOtherErrorCount++;
            }
        }

        return result;
    }

    // The checks every file must pass before it may be touched, in order.
    private sealed class SafetyPolicy
    {
        private readonly List<string> _roots;
        private readonly DateTime _cutoff;

        public SafetyPolicy(IEnumerable<string> allowedRoots, int minAgeDays)
        {
            _roots = allowedRoots
                .Select(r => Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar))
                .ToList();
            _cutoff = DateTime.Now.AddDays(-minAgeDays);
        }

        public SafetyCheck Check(FileInfo info)
        {
            // 1: the file must live inside one of the known/allowed roots.
            // This makes it physically impossible to delete anything outside cache folders,
            // even if a bug elsewhere passes in a bad path.
            var fullPath = info.FullName;
            bool insideAllowedRoot = _roots.Any(root =>
                fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            if (!insideAllowedRoot)
                return SafetyCheck.OutsideRoot;

            // 2: never touch protected file types
            if (ProtectedExtensions.Contains(info.Extension))
                return SafetyCheck.ProtectedExtension;

            if (!info.Exists)
                return SafetyCheck.Missing;

            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return SafetyCheck.ReparsePoint;

            // 3: skip anything accessed more recently than the age threshold
            if (info.LastAccessTime > _cutoff)
                return SafetyCheck.TooRecent;

            return SafetyCheck.Passed;
        }
    }

    private static void RecordSkip(SkipStatistics stats, SafetyCheck check)
    {
        switch (check)
        {
            case SafetyCheck.OutsideRoot: stats.SkippedOutsideRootCount++; break;
            case SafetyCheck.ProtectedExtension: stats.SkippedProtectedCount++; break;
            case SafetyCheck.Missing: stats.SkippedMissingCount++; break;
            case SafetyCheck.ReparsePoint: stats.SkippedReparsePointCount++; break;
            case SafetyCheck.TooRecent: stats.SkippedTooRecentCount++; break;
        }
    }

    // Only blocks that point at a suspicious selection are worth reporting as errors
    private static string? BlockedMessage(SafetyCheck check) => check switch
    {
        SafetyCheck.OutsideRoot => "Blocked (outside allowed roots)",
        SafetyCheck.ProtectedExtension => "Blocked (protected extension)",
        SafetyCheck.ReparsePoint => "Blocked (reparse point)",
        _ => null
    };

    private static bool CanLikelyDelete(FileInfo info, out DenyReason reason)
    {
        // A read-only file always refuses write access, but DeleteFiles clears that flag,
        // so probe it for locks only instead of misreporting it as needing administrator rights.
        var probeAccess = info.IsReadOnly ? FileAccess.Read : FileAccess.Write;

        try
        {
            using var stream = info.Open(FileMode.Open, probeAccess, FileShare.None);
            reason = default;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            reason = DenyReason.AccessDenied;
            return false;
        }
        catch (IOException)
        {
            reason = DenyReason.InUse;
            return false;
        }
    }
}
