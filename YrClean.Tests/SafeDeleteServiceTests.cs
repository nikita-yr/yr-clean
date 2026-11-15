using System.Diagnostics;
using YrClean.Core.Models;
using YrClean.Core.Services;
using Xunit;

namespace YrClean.Tests;

public class SafeDeleteServiceTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly QuarantineService _quarantine;

    public SafeDeleteServiceTests() => _quarantine = new QuarantineService(_temp.QuarantineRoot);

    public void Dispose() => _temp.Dispose();

    private DeleteResult Delete(string file, string? root = null, params string[] excluded) =>
        SafeDeleteService.DeleteFiles(new[] { file }, new[] { root ?? _temp.CacheRoot }, 14, excluded, _quarantine);

    [Fact]
    public void Old_file_is_moved_to_quarantine_not_deleted()
    {
        var file = _temp.AddFile("a.tmp");

        var result = Delete(file);

        Assert.Equal(1, result.DeletedCount);
        Assert.False(File.Exists(file));
        var batch = Assert.Single(_quarantine.ListBatches());
        Assert.Equal(result.QuarantineBatchId, batch.Id);
        Assert.Equal(file, Assert.Single(batch.Entries).OriginalPath);
    }

    [Fact]
    public void Recent_file_is_kept()
    {
        var file = _temp.AddFile("a.tmp", ageDays: 1);

        var result = Delete(file);

        Assert.Equal(1, result.SkippedTooRecentCount);
        Assert.True(File.Exists(file));
        Assert.Null(result.QuarantineBatchId);
    }

    [Fact]
    public void Recently_written_file_is_kept_even_if_last_access_is_old()
    {
        var file = _temp.AddFile("a.tmp");
        File.SetLastWriteTime(file, DateTime.Now);
        File.SetLastAccessTime(file, DateTime.Now.AddDays(-30));

        Assert.Equal(1, Delete(file).SkippedTooRecentCount);
    }

    [Theory]
    [InlineData("a.exe")]
    [InlineData("a.DLL")]
    [InlineData("a.scr")]
    [InlineData("a.lnk")]
    public void Protected_extension_is_kept(string name)
    {
        var file = _temp.AddFile(name);

        Assert.Equal(1, Delete(file).SkippedProtectedCount);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void File_outside_allowed_root_is_kept()
    {
        var file = _temp.AddFile("a.tmp");
        var otherRoot = Directory.CreateDirectory(Path.Combine(_temp.Root, "other")).FullName;

        Assert.Equal(1, Delete(file, otherRoot).SkippedOutsideRootCount);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Sibling_folder_sharing_the_root_prefix_is_outside_the_root()
    {
        var sibling = Directory.CreateDirectory(_temp.CacheRoot + "-not").FullName;
        var file = Path.Combine(sibling, "a.tmp");
        File.WriteAllText(file, "data");
        TempFolder.Age(file, 30);

        Assert.Equal(1, Delete(file).SkippedOutsideRootCount);
    }

    [Fact]
    public void Excluded_subfolder_is_kept()
    {
        var file = _temp.AddFile(@"keep\a.tmp");

        var result = Delete(file, null, Path.Combine(_temp.CacheRoot, "keep"));

        Assert.Equal(1, result.SkippedExcludedCount);
        Assert.True(File.Exists(file));
    }

    [Theory]
    [InlineData(@"%SystemRoot%\System32")]
    [InlineData(@"%SystemRoot%")]
    [InlineData(@"%ProgramFiles%")]
    [InlineData(@"%SystemDrive%\")]
    [InlineData(@"%UserProfile%")]
    public void Rule_rooted_in_a_protected_location_can_delete_nothing(string rootTemplate)
    {
        var root = Environment.ExpandEnvironmentVariables(rootTemplate);
        // Never created: the policy must refuse on location alone, before touching the disk
        var file = Path.Combine(root, "YrCleanTests-" + Guid.NewGuid().ToString("N"), "a.tmp");

        Assert.Equal(1, Delete(file, root).SkippedOutsideRootCount);
    }

    [Fact]
    public void Only_listed_cache_folders_inside_windows_are_allowed()
    {
        Assert.False(ProtectedLocations.IsForbiddenRoot(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Temp")));
        Assert.True(ProtectedLocations.IsForbiddenRoot(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\config")));
    }

    [Fact]
    public void File_behind_a_junction_is_kept()
    {
        var target = Directory.CreateDirectory(Path.Combine(_temp.Root, "target")).FullName;
        var targetFile = Path.Combine(target, "a.tmp");
        File.WriteAllText(targetFile, "data");
        TempFolder.Age(targetFile, 30);

        var link = Path.Combine(_temp.CacheRoot, "link");
        var mklink = new ProcessStartInfo("cmd.exe") { CreateNoWindow = true, UseShellExecute = false };
        foreach (var arg in new[] { "/c", "mklink", "/J", link, target })
            mklink.ArgumentList.Add(arg);
        using (var process = Process.Start(mklink)!)
            process.WaitForExit();
        Assert.True(Directory.Exists(link));

        var result = Delete(Path.Combine(link, "a.tmp"));

        Assert.Equal(1, result.SkippedReparsePointCount);
        Assert.True(File.Exists(targetFile));
        Directory.Delete(link); // removes the junction only
    }

    [Fact]
    public void Classify_applies_the_same_policy()
    {
        var old = _temp.AddFile("old.tmp");
        var recent = _temp.AddFile("recent.tmp", ageDays: 0);
        var exe = _temp.AddFile("tool.exe");

        var result = SafeDeleteService.Classify(new[] { old, recent, exe }, new[] { _temp.CacheRoot }, 14);

        Assert.Equal(new[] { old }, result.DeletableNow);
        Assert.Equal(1, result.SkippedTooRecentCount);
        Assert.Equal(1, result.SkippedProtectedCount);
    }
}
