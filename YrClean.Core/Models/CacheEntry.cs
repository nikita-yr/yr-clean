namespace YrClean.Core.Models;

public class CacheEntry
{
    public string FullPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime LastAccessTime { get; set; }
}
