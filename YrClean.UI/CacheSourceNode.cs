using YrClean.Core.Models;

namespace YrClean.UI;

public class CacheSourceNode : FolderNodeBase
{
    public List<CacheGroupNode> Children { get; }

    public CacheSourceNode(CacheSource source, List<CacheGroup> groups, string primaryPath)
        : base($"{source.Name}  [{source.Category}]",
               primaryPath,
               groups.Sum(g => g.TotalSizeBytes),
               groups.Sum(g => g.FileCount))
    {
        Children = groups.Select(g => new CacheGroupNode(g)).ToList();
    }

    protected override IEnumerable<SelectableNodeBase> GetChildren() => Children;
}
