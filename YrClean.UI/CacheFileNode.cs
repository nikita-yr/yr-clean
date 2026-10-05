using System.IO;
using YrClean.Core.Models;
using YrClean.Core.Services;

namespace YrClean.UI;

public class CacheFileNode : SelectableNodeBase
{
    public string FullPath { get; }

    public CacheFileNode(CacheEntry entry)
        : base(Path.GetFileName(entry.FullPath), SizeFormatter.Format(entry.SizeBytes))
    {
        FullPath = entry.FullPath;
    }

    protected override IEnumerable<SelectableNodeBase> GetChildren() => Array.Empty<SelectableNodeBase>();
}
