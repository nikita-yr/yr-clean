using YrClean.Core.Services;

namespace YrClean.Core.Models;

public class CacheSource
{
    public string Name { get; set; } = string.Empty;

    // May contain %ENV_VAR% placeholders and a single "*" wildcard segment
    public string PathTemplate { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    // Expands env vars, then resolves any wildcard segment into all matching real paths
    public List<string> ResolvePaths()
    {
        var expanded = Environment.ExpandEnvironmentVariables(PathTemplate);
        return PathWildcardResolver.Resolve(expanded);
    }

    // Same as ResolvePaths, minus anything the user has excluded
    public List<string> ResolvePaths(IEnumerable<string> excludedPaths) =>
        ResolvePaths()
            .Where(path => !ExclusionFilter.IsExcluded(path, excludedPaths))
            .ToList();
}
