using YrClean.Core.Services;
using Xunit;

namespace YrClean.Tests;

// The whole cleanup pipeline on a scratch folder: rule file -> scan -> safe delete -> quarantine -> restore.
public class EndToEndTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Rule_file_drives_scan_quarantine_and_restore()
    {
        var old = _temp.AddFile(@"app\Cache\old.bin");
        var oldLog = _temp.AddFile(@"app\Cache\sub\old.log");
        var recent = _temp.AddFile(@"app\Cache\recent.bin", ageDays: 1);
        var program = _temp.AddFile(@"app\Cache\helper.exe");
        var kept = _temp.AddFile(@"app\Cache\keep\settings.bin");
        var cache = Path.Combine(_temp.CacheRoot, "app", "Cache");

        var json = "{ \"schema\": 1, \"rules\": [ { \"id\": \"e2e\", \"name\": \"E2E\", \"level\": \"safe\", " +
                   "\"paths\": [\"" + cache.Replace(@"\", @"\\") + "\"], " +
                   "\"exclude\": [\"" + Path.Combine(cache, "keep").Replace(@"\", @"\\") + "\"] } ] }";
        var rules = RuleEngine.Parse(json, out var errors);
        Assert.Empty(errors);

        var scan = Assert.Single(SourceScanner.Scan(rules, Array.Empty<string>()));
        Assert.DoesNotContain(kept, scan.Files); // the rule's own exclusion is not even listed

        var quarantine = new QuarantineService(_temp.QuarantineRoot);
        var result = SafeDeleteService.DeleteFiles(scan.Files.ToList(), scan.Roots, 14, null, quarantine);

        Assert.Equal(2, result.DeletedCount);
        Assert.Equal(1, result.SkippedTooRecentCount);
        Assert.Equal(1, result.SkippedProtectedCount);
        Assert.False(File.Exists(old));
        Assert.False(File.Exists(oldLog));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(program));
        Assert.True(File.Exists(kept));

        var batch = Assert.Single(quarantine.ListBatches());
        var restore = quarantine.Restore(batch.Id, batch.Entries.Select(e => e.StoredName));

        Assert.Equal(2, restore.RestoredCount);
        Assert.Empty(restore.Errors);
        Assert.Equal("data", File.ReadAllText(old));
        Assert.Equal("data", File.ReadAllText(oldLog));
        Assert.Empty(quarantine.ListBatches());
    }
}
