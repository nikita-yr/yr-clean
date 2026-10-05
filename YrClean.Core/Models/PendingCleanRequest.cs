namespace YrClean.Core.Models;

public class PendingCleanRequest
{
    public List<string> FilePaths { get; set; } = new();
    public List<string> AllowedRoots { get; set; } = new();
    public int MinAgeDays { get; set; }
}
