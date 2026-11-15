using Microsoft.Win32;

namespace YrClean.Core.Services;

public record InstalledProgram(string Name, string? InstallLocation, string? Publisher);

// What is installed on this machine, gathered read-only from the Uninstall registry keys,
// the AppX package repository and Start menu shortcuts.
public static class InstalledPrograms
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string AppxKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    private static readonly Lazy<List<InstalledProgram>> Cache = new(Load);

    public static IReadOnlyList<InstalledProgram> All => Cache.Value;

    public static bool IsInstalled(string namePart) =>
        namePart.Length > 0 && All.Any(p => p.Name.Contains(namePart, StringComparison.OrdinalIgnoreCase));

    private static List<InstalledProgram> Load()
    {
        var programs = new List<InstalledProgram>();

        foreach (var (hive, view) in new[]
                 {
                     (RegistryHive.LocalMachine, RegistryView.Registry64),
                     (RegistryHive.LocalMachine, RegistryView.Registry32),
                     (RegistryHive.CurrentUser, RegistryView.Default)
                 })
        {
            Try(() =>
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(UninstallKey);
                foreach (var name in uninstall?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    using var key = uninstall!.OpenSubKey(name);
                    if (key?.GetValue("DisplayName") is string displayName && displayName.Length > 0)
                    {
                        programs.Add(new InstalledProgram(
                            displayName,
                            key.GetValue("InstallLocation") as string,
                            key.GetValue("Publisher") as string));
                    }
                }
            });
        }

        Try(() =>
        {
            using var packages = Registry.CurrentUser.OpenSubKey(AppxKey);
            foreach (var fullName in packages?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var key = packages!.OpenSubKey(fullName);
                // "Publisher.App_1.2.3.0_x64__hash" -> "Publisher.App"
                programs.Add(new InstalledProgram(fullName.Split('_')[0], key?.GetValue("PackageRootFolder") as string, null));
            }
        });

        foreach (var folder in new[] { Environment.SpecialFolder.CommonPrograms, Environment.SpecialFolder.Programs })
        {
            Try(() =>
            {
                var path = Environment.GetFolderPath(folder);
                if (path.Length == 0 || !Directory.Exists(path))
                    return;

                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
                foreach (var shortcut in Directory.EnumerateFiles(path, "*.lnk", options))
                    programs.Add(new InstalledProgram(Path.GetFileNameWithoutExtension(shortcut), null, null));
            });
        }

        return programs;
    }

    private static void Try(Action read)
    {
        try
        {
            read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // One unreadable source must not hide the others
        }
    }
}
