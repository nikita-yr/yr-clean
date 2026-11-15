using YrClean.Core.Models;

namespace YrClean.Core.Services;

// The single path by which YrClean removes files. Nothing is deleted here: files that pass every
// safety check are moved to quarantine, and only the quarantine's retention purge deletes for real.
public static class SafeDeleteService
{
    // File types that must never be removed, even if they somehow end up selected
    // inside a cache folder — protects against breaking the system or an app.
    private static readonly HashSet<string> ProtectedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".sys", ".msi", ".msp", ".ocx", ".drv", ".com", ".scr", ".cpl", ".efi", ".mui",
        ".lnk", ".ini", ".reg", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".jse", ".wsf", ".hta"
    };

    private enum SafetyCheck
    {
        Passed,
        OutsideRoot,
        Excluded,
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
        int minAgeDays,
        IEnumerable<string>? excludedPaths = null)
    {
        var result = new ClassificationResult();
        var policy = new SafetyPolicy(allowedRoots, minAgeDays, excludedPaths);

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

    // Moves every file that passes the safety policy into one quarantine batch.
    public static DeleteResult DeleteFiles(
        IEnumerable<string> filePaths,
        IEnumerable<string> allowedRoots,
        int minAgeDays,
        IEnumerable<string>? excludedPaths = null,
        QuarantineService? quarantine = null)
    {
        var result = new DeleteResult();
        var policy = new SafetyPolicy(allowedRoots, minAgeDays, excludedPaths);
        using var batch = (quarantine ?? QuarantineService.Default).BeginBatch();

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

                // Like `del /f`: the read-only flag makes the move throw "access denied" even for an
                // administrator. The file has already passed every safety check above.
                if (info.IsReadOnly)
                    info.IsReadOnly = false;

                batch.Add(info);
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

        result.QuarantineBatchId = batch.IsEmpty ? null : batch.Id;
        return result;
    }

    // Why a quarantined file may not be put back at this path, or null when it may.
    // Restoring writes files while elevated, so it gets the same location rules as removing.
    public static string? RestoreBlockReason(string destinationPath)
    {
        if (ProtectedExtensions.Contains(Path.GetExtension(destinationPath)))
            return "Blocked (protected extension)";
        if (ProtectedLocations.IsForbidden(destinationPath))
            return "Blocked (protected location)";
        if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
            return "Already exists";
        if (ProtectedLocations.HasReparsePoint(Path.GetDirectoryName(destinationPath)!))
            return "Blocked (reparse point)";
        return null;
    }

    // The checks every file must pass before it may be touched, in order.
    private sealed class SafetyPolicy
    {
        private readonly List<string> _roots;
        private readonly List<string> _excluded;
        private readonly DateTime _cutoff;
        private readonly Dictionary<string, bool> _reparseByFolder = new(StringComparer.OrdinalIgnoreCase);

        public SafetyPolicy(IEnumerable<string> allowedRoots, int minAgeDays, IEnumerable<string>? excludedPaths)
        {
            // A root pointing at a protected location (System32, Program Files, a drive root, ...)
            // is dropped here, so no rule - built-in or downloaded - can make it deletable.
            _roots = allowedRoots
                .Select(ProtectedLocations.Normalize)
                .Where(root => root.Length > 0 && !ProtectedLocations.IsForbiddenRoot(root))
                .ToList();
            _excluded = (excludedPaths ?? Enumerable.Empty<string>()).ToList();
            _cutoff = DateTime.Now.AddDays(-minAgeDays);
        }

        public SafetyCheck Check(FileInfo info)
        {
            // 1: the file must live inside one of the known/allowed roots.
            // This makes it physically impossible to remove anything outside cache folders,
            // even if a bug elsewhere passes in a bad path.
            var fullPath = info.FullName;
            var root = _roots.FirstOrDefault(r =>
                fullPath.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            if (root is null || ProtectedLocations.IsForbidden(fullPath))
                return SafetyCheck.OutsideRoot;

            // 2: the user's exclusions beat everything else
            if (ExclusionFilter.IsExcluded(fullPath, _excluded))
                return SafetyCheck.Excluded;

            // 3: never touch protected file types
            if (ProtectedExtensions.Contains(info.Extension))
                return SafetyCheck.ProtectedExtension;

            if (!info.Exists)
                return SafetyCheck.Missing;

            // 4: neither the file nor any folder between it and its root may be a link
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || FolderHasReparsePoint(info.DirectoryName!, root))
                return SafetyCheck.ReparsePoint;

            // 5: skip anything read or written more recently than the age threshold. Windows often
            // does not update last-access times, so the later of the two timestamps is used.
            var lastUsed = info.LastAccessTime > info.LastWriteTime ? info.LastAccessTime : info.LastWriteTime;
            if (lastUsed > _cutoff)
                return SafetyCheck.TooRecent;

            return SafetyCheck.Passed;
        }

        private bool FolderHasReparsePoint(string folder, string root)
        {
            if (!_reparseByFolder.TryGetValue(folder, out var hasReparsePoint))
                _reparseByFolder[folder] = hasReparsePoint = ProtectedLocations.HasReparsePoint(folder, stopAt: root);
            return hasReparsePoint;
        }
    }

    private static void RecordSkip(SkipStatistics stats, SafetyCheck check)
    {
        switch (check)
        {
            case SafetyCheck.OutsideRoot: stats.SkippedOutsideRootCount++; break;
            case SafetyCheck.Excluded: stats.SkippedExcludedCount++; break;
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
