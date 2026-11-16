using YrClean.Core.Services;

namespace YrClean.Tests;

// A scratch cache root plus a private quarantine, removed after each test.
public sealed class TempFolder : IDisposable
{
    // Canonical (long-name) form, like every path the app reports back
    public string Root { get; } = ProtectedLocations.Normalize(Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "YrCleanTests", Guid.NewGuid().ToString("N"))).FullName);

    public string CacheRoot => Directory.CreateDirectory(Path.Combine(Root, "cache")).FullName;
    public string QuarantineRoot => Path.Combine(Root, "quarantine");

    // Creates a file whose timestamps are ageDays in the past.
    public string AddFile(string relativePath, int ageDays = 30)
    {
        var path = Path.Combine(CacheRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "data");
        Age(path, ageDays);
        return path;
    }

    public static void Age(string path, int ageDays)
    {
        var stamp = DateTime.Now.AddDays(-ageDays);
        File.SetLastWriteTime(path, stamp);
        File.SetLastAccessTime(path, stamp);
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
