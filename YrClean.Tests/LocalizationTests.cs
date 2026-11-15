using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace YrClean.Tests;

// The UI project is not referenced here (it is a WPF app), so the resource files and the
// sources that use them are checked as files.
public class LocalizationTests
{
    private static readonly string UiFolder = FindUiFolder();
    private static readonly string[] Cultures = { "uk-UA", "ru-RU", "pl-PL" };

    private static string FindUiFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "YrClean.UI");
            if (Directory.Exists(Path.Combine(candidate, "Resources")))
                return candidate;
        }

        throw new DirectoryNotFoundException("YrClean.UI/Resources");
    }

    private static Dictionary<string, string> Load(string? culture) =>
        XDocument.Load(Path.Combine(UiFolder, "Resources", culture is null ? "Strings.resx" : $"Strings.{culture}.resx"))
            .Root!.Elements("data")
            .ToDictionary(d => d.Attribute("name")!.Value, d => d.Element("value")!.Value);

    private static IEnumerable<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Distinct().Order();

    [Fact]
    public void Every_language_has_exactly_the_english_keys_with_the_same_placeholders()
    {
        var english = Load(null);
        foreach (var culture in Cultures)
        {
            var translated = Load(culture);
            Assert.Equal(english.Keys.Order(), translated.Keys.Order());
            foreach (var (key, text) in english)
            {
                Assert.False(string.IsNullOrWhiteSpace(translated[key]), $"{culture}: {key} is empty");
                Assert.True(Placeholders(text).SequenceEqual(Placeholders(translated[key])), $"{culture}: {key} placeholders differ");
            }
        }
    }

    [Fact]
    public void Every_key_used_in_code_or_xaml_is_defined()
    {
        var defined = Load(null).Keys.ToHashSet();
        var used = Directory.EnumerateFiles(UiFolder, "*.*")
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".xaml"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"(?:\bT\(|\{local:Loc\s+|=>\s*)""?([A-Z][A-Za-z0-9]*_[A-Za-z0-9]+)")
                .Select(m => m.Groups[1].Value))
            .ToHashSet();

        Assert.True(used.Count > 150, "the key scan found suspiciously few keys");
        Assert.Empty(used.Except(defined));
        Assert.Empty(defined.Except(used));
    }
}

public class LocalizationBuildTests
{
    // If the embedded resource name ever drifts from the one L.T looks up, every label would
    // silently show its key instead of text. Checked against the built UI assembly.
    [Fact]
    public void Built_ui_assembly_embeds_the_strings_under_the_expected_name()
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name; // Debug or Release
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "YrClean.UI")))
            root = root.Parent;

        var uiOutput = Path.Combine(root!.FullName, "YrClean.UI", "bin", configuration, "net8.0-windows");
        var assembly = Path.Combine(uiOutput, "YrClean.UI.dll");
        if (!File.Exists(assembly))
            return; // tests were run without building the UI project

        var resources = System.Reflection.Assembly.LoadFile(assembly).GetManifestResourceNames();
        Assert.Contains("YrClean.UI.Resources.Strings.resources", resources);
        foreach (var culture in new[] { "uk-UA", "ru-RU", "pl-PL" })
            Assert.True(File.Exists(Path.Combine(uiOutput, culture, "YrClean.UI.resources.dll")), culture);
    }
}
