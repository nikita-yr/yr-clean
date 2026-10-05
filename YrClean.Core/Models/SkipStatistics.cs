namespace YrClean.Core.Models;

// Reasons a file was left alone, shared by cleanup previews and actual deletions.
public abstract class SkipStatistics
{
    public int SkippedTooRecentCount { get; set; }
    public int SkippedProtectedCount { get; set; }
    public int SkippedOutsideRootCount { get; set; }
    public int SkippedMissingCount { get; set; }
    public int SkippedReparsePointCount { get; set; }
    public int SkippedInUseCount { get; set; }
}
