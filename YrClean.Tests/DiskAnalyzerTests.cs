using YrClean.Core.Services;
using Xunit;

namespace YrClean.Tests;

public class DiskAnalyzerTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Write(string relativePath, int bytes, byte fill = 1)
    {
        var path = Path.Combine(_temp.CacheRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Enumerable.Repeat(fill, bytes).ToArray());
        return path;
    }

    [Fact]
    public void Analyze_ranks_top_level_folders_and_largest_files()
    {
        Write(@"big\one.bin", 3000);
        Write(@"big\deep\two.bin", 2000);
        Write(@"small\three.bin", 100);
        var rootFile = Write("root.bin", 4000);

        var analysis = DiskAnalyzer.Analyze(_temp.CacheRoot, top: 2);

        Assert.Equal(new[] { "big", "small" }, analysis.Folders.Select(f => Path.GetFileName(f.Path)));
        Assert.Equal(5000, analysis.Folders[0].Bytes);
        Assert.Equal(new long[] { 4000, 3000 }, analysis.Files.Select(f => f.Bytes));
        Assert.Equal(rootFile, analysis.Files[0].Path);
    }

    [Fact]
    public void Old_files_are_those_not_modified_within_the_period()
    {
        var old = _temp.AddFile("old.zip", ageDays: 200);
        _temp.AddFile("new.zip", ageDays: 5);

        Assert.Equal(new[] { old }, DiskAnalyzer.OldFiles(_temp.CacheRoot, 90).Select(f => f.Path));
    }

    [Fact]
    public void Duplicates_are_grouped_by_content_not_just_size()
    {
        var a = Write("a.bin", 2000, fill: 7);
        var b = Write(@"copy\b.bin", 2000, fill: 7);
        Write("same-size-other-content.bin", 2000, fill: 9);
        Write("tiny1.bin", 10);
        Write("tiny2.bin", 10);

        var group = Assert.Single(DiskAnalyzer.FindDuplicates(_temp.CacheRoot, minBytes: 1000));

        Assert.Equal(new[] { a, b }.Order(), group.Select(f => f.Path).Order());
    }

    [Fact]
    public void Low_space_uses_the_threshold_percentage()
    {
        Assert.Empty(DiskAnalyzer.LowSpaceDrives(0));
        Assert.Equal(SystemHealth.GetDriveSpace().Count, DiskAnalyzer.LowSpaceDrives(101).Count);
    }
}
