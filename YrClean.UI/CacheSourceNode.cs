using YrClean.Core.Models;

namespace YrClean.UI;

public class CacheSourceNode : FolderNodeBase
{
    public List<CacheGroupNode> Children { get; }
    public RuleLevel Level { get; }

    public CacheSourceNode(CacheSource source, List<CacheGroup> groups, string primaryPath)
        : base($"{source.Name}  [{source.Category}]{LevelSuffix(source.Level)}",
               primaryPath,
               groups.Sum(g => g.TotalSizeBytes),
               groups.Sum(g => g.FileCount))
    {
        Level = source.Level;
        Children = groups.Select(g => new CacheGroupNode(g)).ToList();
    }

    // Safe is the unmarked default; the riskier levels are called out in the row itself
    private static string LevelSuffix(RuleLevel level) => level switch
    {
        RuleLevel.Review => "  · review",
        RuleLevel.Expert => "  · expert",
        _ => ""
    };

    protected override IEnumerable<SelectableNodeBase> GetChildren() => Children;
}
