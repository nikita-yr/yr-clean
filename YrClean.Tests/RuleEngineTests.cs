using YrClean.Core.Models;
using YrClean.Core.Services;
using Xunit;

namespace YrClean.Tests;

public class RuleEngineTests
{
    private static string File(string rulesJson) => "{ \"schema\": 1, \"rules\": [" + rulesJson + "] }";

    private static List<CacheSource> ParseRules(string rulesJson, out List<string> errors) =>
        RuleEngine.Parse(File(rulesJson), out errors);

    [Fact]
    public void Built_in_rules_all_load_and_are_valid()
    {
        var rules = RuleEngine.LoadBuiltIn();

        Assert.True(rules.Count >= 25);
        Assert.All(rules, rule => Assert.Null(RuleEngine.Validate(rule)));
        Assert.Equal(rules.Count, rules.Select(r => r.Id).Distinct().Count());
        Assert.Equal(RuleLevel.Expert, rules.Single(r => r.Id == "prefetch").Level);
    }

    [Fact]
    public void Chromium_rule_expands_to_one_rule_per_browser()
    {
        var rules = RuleEngine.LoadBuiltIn();

        var chrome = rules.Single(r => r.Id == "chromium-chrome");
        Assert.Equal("Chrome Cache", chrome.Name);
        Assert.Equal(new[] { "chrome" }, chrome.Process);
        Assert.Contains(chrome.Paths, p => p.Path == @"%LocalAppData%\Google\Chrome\User Data\*\Cache\Cache_Data");
        foreach (var browser in new[] { "edge", "brave", "opera", "vivaldi", "yandex" })
            Assert.Contains(rules, r => r.Id == "chromium-" + browser);
    }

    [Fact]
    public void Parses_all_fields_and_both_path_forms()
    {
        var rules = ParseRules("""
            {
              "id": "my-app", "name": "My App", "category": "Apps", "level": "review",
              "detect": ["app:My App"], "process": ["myapp"], "minAgeDays": 30,
              "paths": [
                "%LocalAppData%\\MyApp\\Cache",
                { "path": "%LocalAppData%\\MyApp\\Logs", "masks": ["*.log"], "recurse": false }
              ]
            }
            """, out var errors);

        Assert.Empty(errors);
        var rule = Assert.Single(rules);
        Assert.Equal(RuleLevel.Review, rule.Level);
        Assert.Equal(30, rule.MinAgeDays);
        Assert.Equal(new[] { "myapp" }, rule.Process);
        Assert.True(rule.Paths[0].Recurse);
        Assert.Empty(rule.Paths[0].Masks);
        Assert.False(rule.Paths[1].Recurse);
        Assert.Equal(new[] { "*.log" }, rule.Paths[1].Masks);
    }

    [Theory]
    [InlineData(@"%SystemRoot%\\System32")]
    [InlineData(@"%SystemRoot%\\System32\\config")]
    [InlineData(@"%ProgramFiles%\\SomeApp\\cache")]
    [InlineData(@"%ProgramFiles(x86)%\\SomeApp")]
    [InlineData(@"%SystemDrive%\\")]
    [InlineData(@"%SystemDrive%\\*\\cache")]
    [InlineData(@"%UserProfile%")]
    [InlineData(@"%UserProfile%\\Documents\\Stuff")]
    [InlineData(@"%LocalAppData%\\..\\..\\Documents")]
    [InlineData(@"relative\\cache")]
    [InlineData(@"\\\\server\\share\\cache")]
    [InlineData(@"%NoSuchVariable%\\cache")]
    public void Rule_pointing_at_a_forbidden_or_malformed_path_is_rejected(string jsonPath)
    {
        var rules = ParseRules($$"""{ "id": "bad", "name": "Bad", "paths": ["{{jsonPath}}"] }""", out var errors);

        Assert.Empty(rules);
        Assert.Single(errors);
    }

    [Fact]
    public void Allowed_windows_cache_folder_is_accepted()
    {
        var rules = ParseRules("""{ "id": "ok", "name": "Ok", "paths": ["%SystemRoot%\\Temp"] }""", out var errors);

        Assert.Empty(errors);
        Assert.Single(rules);
    }

    [Theory]
    [InlineData("""{ "id": "Bad Id", "name": "x", "paths": ["%TEMP%\\a"] }""")]
    [InlineData("""{ "id": "no-name", "paths": ["%TEMP%\\a"] }""")]
    [InlineData("""{ "id": "no-paths", "name": "x" }""")]
    [InlineData("""{ "id": "bad-level", "name": "x", "level": "yolo", "paths": ["%TEMP%\\a"] }""")]
    [InlineData("""{ "id": "bad-age", "name": "x", "minAgeDays": -1, "paths": ["%TEMP%\\a"] }""")]
    [InlineData("""{ "id": "bad-mask", "name": "x", "paths": [{ "path": "%TEMP%\\a", "masks": ["..\\*.dll"] }] }""")]
    public void Invalid_rule_is_dropped_but_valid_neighbours_survive(string badRule)
    {
        var rules = ParseRules(badRule + """, { "id": "good", "name": "Good", "paths": ["%TEMP%\\good"] }""", out var errors);

        Assert.Equal("good", Assert.Single(rules).Id);
        Assert.Single(errors);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "rules": [] }""")]
    [InlineData("""{ "schema": 99, "rules": [] }""")]
    [InlineData("""{ "schema": 1, "rules": {} }""")]
    public void File_with_wrong_shape_or_schema_yields_nothing(string json)
    {
        Assert.Empty(RuleEngine.Parse(json, out var errors));
        Assert.Single(errors);
    }

    [Fact]
    public void Detect_decides_relevance()
    {
        var present = new CacheSource { Detect = { "%SystemRoot%" } };
        var absent = new CacheSource { Detect = { @"%SystemRoot%\no-such-folder-yrclean", "app:No Such Program YrClean", @"HKCU\Software\NoSuchKeyYrClean" } };
        var registry = new CacheSource { Detect = { @"HKLM\SOFTWARE\Microsoft\Windows" } };

        Assert.True(RuleEngine.IsRelevant(new CacheSource()));
        Assert.True(RuleEngine.IsRelevant(present));
        Assert.True(RuleEngine.IsRelevant(registry));
        Assert.False(RuleEngine.IsRelevant(absent));
    }
}

public class SourceScannerTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private CacheSource Rule(string id, RulePath path) => new() { Id = id, Name = id, Paths = { path } };

    [Fact]
    public void Masks_and_recurse_limit_what_is_listed()
    {
        _temp.AddFile("a.log");
        _temp.AddFile("b.txt");
        _temp.AddFile(@"sub\c.log");
        var rule = Rule("r", new RulePath { Path = _temp.CacheRoot, Masks = { "*.log" }, Recurse = false });

        var scan = Assert.Single(SourceScanner.Scan(new[] { rule }, Array.Empty<string>()));

        Assert.Equal(new[] { Path.Combine(_temp.CacheRoot, "a.log") }, scan.Files);
    }

    [Fact]
    public void Running_process_marks_the_rule_busy_and_lists_no_files()
    {
        _temp.AddFile("a.tmp");
        var rule = Rule("r", new RulePath { Path = _temp.CacheRoot });
        rule.Process.Add(System.Diagnostics.Process.GetCurrentProcess().ProcessName);

        var scan = Assert.Single(SourceScanner.Scan(new[] { rule }, Array.Empty<string>()));

        Assert.True(scan.Busy);
        Assert.Empty(scan.Files);
    }

    [Fact]
    public void A_folder_is_listed_only_by_the_first_rule_that_claims_it()
    {
        _temp.AddFile(@"sub\a.tmp");
        var first = Rule("first", new RulePath { Path = _temp.CacheRoot });
        var nested = Rule("nested", new RulePath { Path = Path.Combine(_temp.CacheRoot, "sub") });

        var scans = SourceScanner.Scan(new[] { first, nested }, Array.Empty<string>());

        Assert.Equal("first", Assert.Single(scans).Source.Id);
    }

    [Fact]
    public void Rule_minimum_age_can_only_tighten_the_global_one()
    {
        var file = _temp.AddFile("a.tmp", ageDays: 20);
        var quarantine = new QuarantineService(_temp.QuarantineRoot);
        var roots = new[] { _temp.CacheRoot };

        var stricter = SafeDeleteService.DeleteFiles(new[] { file }, roots, 14, null, quarantine,
            new Dictionary<string, int> { [_temp.CacheRoot] = 30 });
        Assert.Equal(1, stricter.SkippedTooRecentCount);

        var looser = SafeDeleteService.DeleteFiles(new[] { file }, roots, 30, null, quarantine,
            new Dictionary<string, int> { [_temp.CacheRoot] = 1 });
        Assert.Equal(1, looser.SkippedTooRecentCount);
        Assert.True(System.IO.File.Exists(file));
    }
}
