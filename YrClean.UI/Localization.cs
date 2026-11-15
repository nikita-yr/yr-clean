using System.Globalization;
using System.Resources;
using System.Windows.Markup;
using YrClean.Core.Services;

namespace YrClean.UI;

// All user-facing text comes from Resources/Strings*.resx. English is the neutral language;
// the log file and CLI reports stay English on purpose and do not go through here.
public static class L
{
    // "" in settings means "follow Windows"
    public static readonly string[] Languages = { "en", "uk-UA", "ru-RU", "pl-PL" };

    private static readonly ResourceManager Resources = new("YrClean.UI.Resources.Strings", typeof(L).Assembly);
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentUICulture;

    // Looks up a string; "\n" in a resource is a line break, {0}... are filled from args.
    public static string T(string key, params object?[] args)
    {
        var text = (Resources.GetString(key) ?? key).Replace("\\n", "\n");
        return args.Length == 0 ? text : string.Format(text, args);
    }

    // Picks the saved language, else the Windows display language if we ship it, else English.
    public static void Apply(string? language)
    {
        var culture = CultureInfo.GetCultureInfo(Resolve(language, SystemCulture.TwoLetterISOLanguageName));
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    public static string Resolve(string? saved, string systemTwoLetterLanguage) =>
        Languages.FirstOrDefault(l => l.Equals(saved, StringComparison.OrdinalIgnoreCase))
        ?? Languages.FirstOrDefault(l => l.StartsWith(systemTwoLetterLanguage + "-", StringComparison.OrdinalIgnoreCase))
        ?? "en";

    public static string LowSpace(IEnumerable<DriveSpace> drives) => string.Join("; ", drives.Select(d =>
        T("LowSpace_Drive", d.Name, SizeFormatter.Format(d.FreeBytes), d.FreePercent.ToString("F0"))));

    public static string DriveLine(DriveSpace d) =>
        T("Drive_Line", d.Name, SizeFormatter.Format(d.FreeBytes), SizeFormatter.Format(d.TotalBytes), d.FreePercent.ToString("F0"));
}

// XAML: Content="{local:Loc Main_Clean}"
[MarkupExtensionReturnType(typeof(string))]
public class LocExtension : MarkupExtension
{
    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key);
}
