namespace YrClean.Core.Models;

public class ClassificationResult : SkipStatistics
{
    public List<string> DeletableNow { get; set; } = new();
    public long DeletableNowBytes { get; set; }
    public List<string> NeedsAdmin { get; set; } = new();
    public long NeedsAdminBytes { get; set; }

    public int TotalCount => DeletableNow.Count + NeedsAdmin.Count;
    public long TotalBytes => DeletableNowBytes + NeedsAdminBytes;
}
