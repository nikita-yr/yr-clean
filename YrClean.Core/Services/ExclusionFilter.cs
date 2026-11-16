namespace YrClean.Core.Services;

public static class ExclusionFilter
{
    public static bool IsExcluded(string fullPath, IEnumerable<string> excludedPaths)
    {
        var normalized = ProtectedLocations.Normalize(fullPath);

        foreach (var excluded in excludedPaths)
        {
            var ex = ProtectedLocations.Normalize(excluded);
            if (ex.Length == 0)
                continue;

            if (normalized.Equals(ex, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(ex + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}