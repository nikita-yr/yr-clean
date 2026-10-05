using YrClean.Core.Models;

namespace YrClean.UI;

public class CacheGroupNode : FolderNodeBase
{
    public List<CacheFileNode> Children { get; }

    public CacheGroupNode(CacheGroup group)
        : base(group.FolderPath, group.FullFolderPath, group.TotalSizeBytes, group.FileCount)
    {
        Children = group.Files
            .OrderByDescending(f => f.SizeBytes)
            .Select(f => new CacheFileNode(f))
            .ToList();
    }

    protected override IEnumerable<SelectableNodeBase> GetChildren() => Children;
}
