using YrClean.Core.Models;
using YrClean.Core.Services;

namespace YrClean.UI;

public class CacheSourceNode : FolderNodeBase
{
    public List<CacheGroupNode> Children { get; }
    public RuleLevel Level { get; }

    public CacheSourceNode(SourceScan scan)
        : base($"{scan.Source.Name}  [{scan.Source.Category}]{Suffix(scan)}",
               scan.Roots[0],
               scan.TotalBytes,
               scan.Groups.Sum(g => g.FileCount))
    {
        Level = scan.Source.Level;
        Children = scan.Groups.Select(g => new CacheGroupNode(g)).ToList();
    }

    // Safe is the unmarked default; riskier levels and skipped rules are called out in the row itself
    private static string Suffix(SourceScan scan)
    {
        if (scan.Busy)
            return "  · " + L.T("Node_Busy");

        return scan.Source.Level switch
        {
            RuleLevel.Review => "  · " + L.T("Node_Review"),
            RuleLevel.Expert => "  · " + L.T("Node_Expert"),
            _ => ""
        };
    }

    protected override IEnumerable<SelectableNodeBase> GetChildren() => Children;
}
