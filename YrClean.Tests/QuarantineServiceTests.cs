using System.Text.Json;
using YrClean.Core.Models;
using YrClean.Core.Services;
using Xunit;

namespace YrClean.Tests;

public class QuarantineServiceTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly QuarantineService _quarantine;

    public QuarantineServiceTests() => _quarantine = new QuarantineService(_temp.QuarantineRoot);

    public void Dispose() => _temp.Dispose();

    private string Quarantine(params string[] files) =>
        SafeDeleteService.DeleteFiles(files, new[] { _temp.CacheRoot }, 14, null, _quarantine).QuarantineBatchId!;

    // Simulates an unprivileged process editing the manifest behind the app's back
    private void AppendManifestLine(string batchId, string originalPath, string storedName) =>
        File.AppendAllLines(
            Path.Combine(_temp.QuarantineRoot, batchId, "manifest.jsonl"),
            new[] { JsonSerializer.Serialize(new QuarantineEntry(originalPath, storedName, 4)) });

    [Fact]
    public void Restore_puts_the_file_back_with_its_content()
    {
        var file = _temp.AddFile(@"sub\a.tmp");
        var batchId = Quarantine(file);
        Directory.Delete(Path.GetDirectoryName(file)!); // the folder may be gone by restore time

        var result = _quarantine.Restore(batchId, new[] { "0.q" });

        Assert.Equal(1, result.RestoredCount);
        Assert.Equal("data", File.ReadAllText(file));
        Assert.Empty(_quarantine.ListBatches());
        Assert.False(Directory.Exists(Path.Combine(_temp.QuarantineRoot, batchId)));
    }

    [Fact]
    public void Restore_only_touches_the_selected_files()
    {
        var first = _temp.AddFile("a.tmp");
        var second = _temp.AddFile("b.tmp");
        var batchId = Quarantine(first, second);

        _quarantine.Restore(batchId, new[] { "1.q" });

        Assert.False(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.Equal(first, Assert.Single(Assert.Single(_quarantine.ListBatches()).Entries).OriginalPath);
    }

    [Fact]
    public void Restore_never_overwrites_an_existing_file()
    {
        var file = _temp.AddFile("a.tmp");
        var batchId = Quarantine(file);
        File.WriteAllText(file, "newer");

        var result = _quarantine.Restore(batchId, new[] { "0.q" });

        Assert.Equal(0, result.RestoredCount);
        Assert.Single(result.Errors);
        Assert.Equal("newer", File.ReadAllText(file));
        Assert.Single(_quarantine.ListBatches());
    }

    [Theory]
    [InlineData(@"%SystemRoot%\System32\yrclean-test.tmp")]
    [InlineData(@"%Temp%\yrclean-test-payload.dll")]
    public void Restore_refuses_a_tampered_manifest_destination(string destinationTemplate)
    {
        var destination = Environment.ExpandEnvironmentVariables(destinationTemplate);
        var batchId = Quarantine(_temp.AddFile("a.tmp"), _temp.AddFile("b.tmp"));
        File.Delete(Path.Combine(_temp.QuarantineRoot, batchId, "manifest.jsonl"));
        AppendManifestLine(batchId, destination, "0.q");

        var result = _quarantine.Restore(batchId, new[] { "0.q" });

        Assert.Equal(0, result.RestoredCount);
        Assert.Single(result.Errors);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public void Manifest_entry_pointing_outside_the_batch_is_ignored()
    {
        var batchId = Quarantine(_temp.AddFile("a.tmp"));
        var outside = _temp.AddFile("victim.q");
        var escapingName = @"..\..\cache\victim.q";
        AppendManifestLine(batchId, Path.Combine(_temp.CacheRoot, "x.tmp"), escapingName);

        var result = _quarantine.Restore(batchId, new[] { escapingName });

        Assert.Equal(0, result.RestoredCount);
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void Purge_removes_only_batches_past_retention()
    {
        var batchId = Quarantine(_temp.AddFile("a.tmp"));

        Assert.Equal(0, _quarantine.Purge(7));
        Assert.Single(_quarantine.ListBatches());

        Assert.Equal(1, _quarantine.Purge(7, DateTime.UtcNow.AddDays(8)));
        Assert.Empty(_quarantine.ListBatches());
        Assert.False(Directory.Exists(Path.Combine(_temp.QuarantineRoot, batchId)));
    }

    [Fact]
    public void Purge_ignores_folders_it_did_not_create()
    {
        var foreign = Directory.CreateDirectory(Path.Combine(_temp.QuarantineRoot, "not-a-batch")).FullName;

        _quarantine.Purge(0, DateTime.UtcNow.AddDays(1));

        Assert.True(Directory.Exists(foreign));
    }
}
