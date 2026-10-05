using YrClean.Core.Services;

namespace YrClean.UI;

// A tree node that stands for a folder: it has a location on disk and aggregate size/count.
public abstract class FolderNodeBase : SelectableNodeBase
{
    protected FolderNodeBase(string name, string fullFolderPath, long totalSizeBytes, int fileCount)
        : base(name, SizeFormatter.Format(totalSizeBytes))
    {
        FullFolderPath = fullFolderPath;
        TotalSizeBytes = totalSizeBytes;
        CountDisplay = $"{fileCount} files";
    }

    public string FullFolderPath { get; }
    public long TotalSizeBytes { get; }
    public string CountDisplay { get; }
}
