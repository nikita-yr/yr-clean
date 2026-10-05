namespace YrClean.Core.Models;

public class DeleteResult : SkipStatistics
{
    public int DeletedCount { get; set; }
    public long FreedBytes { get; set; }

    public int SkippedAccessDeniedCount { get; set; }
    public int SkippedOtherErrorCount { get; set; }

    public int SkippedCount =>
        SkippedTooRecentCount + SkippedProtectedCount + SkippedOutsideRootCount +
        SkippedMissingCount + SkippedReparsePointCount + SkippedAccessDeniedCount +
        SkippedInUseCount + SkippedOtherErrorCount;

    public List<string> Errors { get; set; } = new();
}
