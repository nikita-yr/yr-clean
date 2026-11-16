namespace YrClean.Core.Services;

public static class CacheHeuristics
{
    private static readonly HashSet<string> ExactCacheNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cache", "caches", "tmp", "temp", "logs", "log",
        "crashpad", "crash dumps", "blob_storage", "diskcache", "webcache", "gpucache"
    };

    public static bool LooksLikeCacheFolder(string folderName)
    {
        if (ExactCacheNames.Contains(folderName)) return true;
        return folderName.ToLowerInvariant().Contains("cache");
    }

    private static readonly string[] ExclusionKeywords =
    {
        "config", "settings", "save", "saves", "profile", "profiles",
        "credentials", "keys", "wallet", "identities", "account", "login data",
        // installer caches: Windows needs them to repair or uninstall programs
        "package cache", "installer"
    };

    public static bool IsExcluded(string fullPath)
    {
        var lower = fullPath.ToLowerInvariant();
        return ExclusionKeywords.Any(lower.Contains);
    }
}