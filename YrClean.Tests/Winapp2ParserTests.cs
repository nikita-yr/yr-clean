using YrClean.Core.Models;
using YrClean.Core.Services;
using Xunit;

namespace YrClean.Tests;

public class Winapp2ParserTests
{
    private const string Sample = """
        ; Version: 250101
        ; comment line

        [Some App *]
        LangSecRef=3021
        Detect=HKCU\Software\SomeApp
        DetectFile1=%LocalAppData%\SomeApp
        FileKey1=%LocalAppData%\SomeApp\Cache|*.*|RECURSE
        FileKey2=%CommonAppData%\SomeApp\Logs|*.log;*.old
        ExcludeKey1=FILE|%LocalAppData%\SomeApp\Cache\|keep.dat
        ExcludeKey2=PATH|%LocalAppData%\SomeApp\Cache\Important\|*.*
        ExcludeKey3=REG|HKCU\Software\SomeApp
        RegKey1=HKCU\Software\SomeApp\Recent

        [Registry Only *]
        Section=Games
        RegKey1=HKCU\Software\Game\MRU

        [Program Files App *]
        Section=Games
        FileKey1=%ProgramFiles%\Game\Temp|*.tmp|REMOVESELF

        [System Killer *]
        FileKey1=%WinDir%\System32|*.*|RECURSE
        """;

    private readonly List<CacheSource> _rules = Winapp2Parser.Parse(Sample);

    [Fact]
    public void Sections_without_file_keys_are_dropped()
    {
        Assert.Equal(new[] { "Some App", "Program Files App", "System Killer" }, _rules.Select(r => r.Name));
    }

    [Fact]
    public void Every_imported_rule_is_review_level_with_a_valid_id()
    {
        Assert.All(_rules, r => Assert.Equal(RuleLevel.Review, r.Level));
        Assert.Equal("winapp2.some-app", _rules[0].Id);
    }

    [Fact]
    public void File_keys_become_paths_with_masks_and_recursion()
    {
        var rule = _rules[0];

        Assert.Equal(@"%LocalAppData%\SomeApp\Cache", rule.Paths[0].Path);
        Assert.Empty(rule.Paths[0].Masks);
        Assert.True(rule.Paths[0].Recurse);

        Assert.Equal(@"%ProgramData%\SomeApp\Logs", rule.Paths[1].Path);
        Assert.Equal(new[] { "*.log", "*.old" }, rule.Paths[1].Masks);
        Assert.False(rule.Paths[1].Recurse);
    }

    [Fact]
    public void Detect_and_exclude_keys_are_kept_and_registry_keys_ignored()
    {
        var rule = _rules[0];

        Assert.Equal(new[] { @"HKCU\Software\SomeApp", @"%LocalAppData%\SomeApp" }, rule.Detect);
        Assert.Equal(
            new[] { @"%LocalAppData%\SomeApp\Cache\keep.dat", @"%LocalAppData%\SomeApp\Cache\Important" },
            rule.Exclude);
    }

    [Fact]
    public void Section_key_sets_the_category_and_program_files_covers_both_folders()
    {
        var rule = _rules[1];

        Assert.Equal("Games", rule.Category);
        Assert.Equal(
            new[] { @"%ProgramFiles%\Game\Temp", @"%ProgramFiles(x86)%\Game\Temp" },
            rule.Paths.Select(p => p.Path));
    }

    [Fact]
    public void Imported_rules_get_the_same_validation_as_any_other_rule()
    {
        Assert.Null(RuleEngine.Validate(_rules[0]));
        Assert.NotNull(RuleEngine.Validate(_rules[1])); // Program Files
        Assert.NotNull(RuleEngine.Validate(_rules[2])); // System32
    }
}
