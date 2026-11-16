using System.Runtime.InteropServices;
using System.Text;
using YrClean.Core.Models;
using YrClean.Core.Services;
using Xunit;

namespace YrClean.Tests;

// 8.3 short names ("C:\PROGRA~1") are a second spelling of the same folder. Every check that
// compares paths must see through them, or a rule could reach a protected location.
public class ShortPathTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetShortPathName(string longPath, StringBuilder shortPath, int bufferLength);

    // Null when the volume has short names turned off or the path has none
    private static string? ShortForm(string path)
    {
        var buffer = new StringBuilder(1024);
        var length = GetShortPathName(path, buffer, buffer.Capacity);
        var result = length > 0 ? buffer.ToString() : null;
        return result is not null && !result.Equals(path, StringComparison.OrdinalIgnoreCase) ? result : null;
    }

    [Fact]
    public void Short_name_of_program_files_is_still_a_protected_location()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var shortForm = ShortForm(programFiles);
        if (shortForm is null)
            return; // nothing to disguise on this volume

        Assert.Contains('~', shortForm);
        Assert.True(ProtectedLocations.IsForbidden(Path.Combine(shortForm, "SomeApp", "cache")));
        Assert.True(ProtectedLocations.IsForbiddenRoot(shortForm));
        Assert.NotNull(RuleEngine.Validate(new CacheSource
        {
            Id = "sneaky", Name = "Sneaky", Paths = { new RulePath { Path = Path.Combine(shortForm, "SomeApp") } }
        }));
    }

    [Fact]
    public void Normalize_expands_short_names_even_when_the_tail_does_not_exist()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_temp.Root, "A Folder With A Long Name")).FullName;
        var shortForm = ShortForm(folder);
        if (shortForm is null)
            return;

        Assert.Equal(folder, ProtectedLocations.Normalize(shortForm));
        Assert.Equal(Path.Combine(folder, "missing", "file.tmp"),
            ProtectedLocations.Normalize(Path.Combine(shortForm, "missing", "file.tmp")));
    }

    [Fact]
    public void Short_and_long_spellings_of_one_folder_are_one_root()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_temp.Root, "A Folder With A Long Name")).FullName;
        var file = Path.Combine(folder, "old.tmp");
        File.WriteAllText(file, "data");
        TempFolder.Age(file, 30);
        var shortForm = ShortForm(folder);
        if (shortForm is null)
            return;

        var longRule = new CacheSource { Id = "long", Name = "long", Paths = { new RulePath { Path = folder } } };
        var shortRule = new CacheSource { Id = "short", Name = "short", Paths = { new RulePath { Path = shortForm } } };

        // The second spelling is recognised as the same folder and not listed again
        Assert.Equal("long", Assert.Single(SourceScanner.Scan(new[] { longRule, shortRule }, Array.Empty<string>())).Source.Id);

        // A file named through the short form is checked against the long-form root, and vice versa
        var quarantine = new QuarantineService(_temp.QuarantineRoot);
        var result = SafeDeleteService.DeleteFiles(
            new[] { Path.Combine(shortForm, "old.tmp") }, new[] { folder }, 14, null, quarantine);
        Assert.Equal(1, result.DeletedCount);
        Assert.Equal(file, Assert.Single(Assert.Single(quarantine.ListBatches()).Entries).OriginalPath);
    }

    [Fact]
    public void Exclusion_written_with_a_short_name_still_protects_the_folder()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_temp.CacheRoot, "Keep This Folder Safe")).FullName;
        var file = Path.Combine(folder, "old.tmp");
        File.WriteAllText(file, "data");
        TempFolder.Age(file, 30);
        var shortForm = ShortForm(folder);
        if (shortForm is null)
            return;

        var result = SafeDeleteService.DeleteFiles(
            new[] { file }, new[] { _temp.CacheRoot }, 14, new[] { shortForm }, new QuarantineService(_temp.QuarantineRoot));

        Assert.Equal(1, result.SkippedExcludedCount);
        Assert.True(File.Exists(file));
    }
}
