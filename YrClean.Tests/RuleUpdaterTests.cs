using YrClean.Core.Services;
using Xunit;

namespace YrClean.Tests;

public class RuleUpdaterTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string RuleFile(int version, string path) =>
        "{ \"schema\": 1, \"version\": " + version + ", \"rules\": [ { \"id\": \"r\", \"name\": \"R\", \"paths\": [\"" + path + "\"] } ] }";

    // Backslashes are doubled because these strings are spliced into JSON
    private const string GoodPath = @"%LocalAppData%\\SomeApp\\Cache";
    private const string System32Path = @"%SystemRoot%\\System32";

    [Theory]
    [InlineData("https://raw.githubusercontent.com/nikita-yr/yr-clean/main/x.json", true)]
    [InlineData("http://raw.githubusercontent.com/nikita-yr/yr-clean/main/x.json", false)]
    [InlineData("file:///C:/rules.json", false)]
    [InlineData("not a url", false)]
    public void Only_https_urls_are_allowed(string url, bool allowed)
    {
        Assert.Equal(allowed, RuleUpdater.IsAllowedUrl(url));
    }

    [Fact]
    public void Built_in_download_locations_are_https()
    {
        Assert.True(RuleUpdater.IsAllowedUrl(RuleUpdater.RulesUrl));
        Assert.True(RuleUpdater.IsAllowedUrl(RuleUpdater.Winapp2Url));
    }

    [Fact]
    public void Valid_rule_file_is_saved()
    {
        var result = RuleUpdater.SaveRules(RuleFile(2, GoodPath), _temp.Root);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(1, result.RuleCount);
        Assert.True(File.Exists(Path.Combine(_temp.Root, RuleEngine.RemoteRulesFileName)));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{ "schema": 2, "rules": [] }""")]
    [InlineData("""{ "schema": 1, "rules": [] }""")]
    public void Malformed_download_is_rejected_and_nothing_is_written(string json)
    {
        Assert.False(RuleUpdater.SaveRules(json, _temp.Root).Ok);
        Assert.False(File.Exists(Path.Combine(_temp.Root, RuleEngine.RemoteRulesFileName)));
    }

    [Fact]
    public void Download_with_a_rule_aimed_at_system32_is_rejected_whole()
    {
        var result = RuleUpdater.SaveRules(RuleFile(9, System32Path), _temp.Root);

        Assert.False(result.Ok);
        Assert.Contains("protected location", result.Error);
        Assert.False(File.Exists(Path.Combine(_temp.Root, RuleEngine.RemoteRulesFileName)));
    }

    [Fact]
    public void Downloaded_rules_replace_built_in_only_when_usable_and_not_older()
    {
        var builtIn = RuleFile(5, GoodPath);

        Assert.Equal(builtIn, RuleEngine.ChooseRuleBase(builtIn, null));
        Assert.Equal(builtIn, RuleEngine.ChooseRuleBase(builtIn, RuleFile(4, GoodPath)));
        Assert.Equal(builtIn, RuleEngine.ChooseRuleBase(builtIn, "garbage"));
        var newer = RuleFile(6, GoodPath);
        Assert.Equal(newer, RuleEngine.ChooseRuleBase(builtIn, newer));
    }

    [Fact]
    public void Winapp2_is_saved_only_if_it_holds_usable_rules()
    {
        const string ini = "[App *]\nFileKey1=%LocalAppData%\\App\\Cache|*.*|RECURSE\n";

        Assert.Equal(1, RuleUpdater.SaveWinapp2(ini, _temp.Root).RuleCount);
        Assert.True(File.Exists(Path.Combine(_temp.Root, RuleEngine.Winapp2FileName)));
        Assert.False(RuleUpdater.SaveWinapp2("<html>404</html>", Path.Combine(_temp.Root, "other")).Ok);
    }
}
