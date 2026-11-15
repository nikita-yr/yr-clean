using Microsoft.Win32;
using YrClean.Core.Models;

namespace YrClean.Core.Services;

public enum LeftoverKind
{
    Folder,
    Service,
    Task,
    Shortcut
}

// Location says where the item lives (folder path, service key name, task name, shortcut path);
// MissingTarget is the program it points at that no longer exists; Path is what Explorer can open.
public record Leftover(LeftoverKind Kind, string Name, string Location, string? MissingTarget, string? Path, long Bytes);

// Finds what uninstalled programs left behind. Everything here is a candidate for a person to
// review: the scan itself changes nothing, and only folders can be removed (via quarantine).
public static class LeftoverScanner
{
    public const int InactiveDays = 30;
    private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services";

    // Folders that belong to Windows or are shared by many programs, never candidates
    private static readonly HashSet<string> SystemFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Windows", "Packages", "Temp", "Programs", "Package Cache", "Microsoft Help",
        "Application Data", "Desktop", "Documents", "Start Menu", "Templates", "ssh", "USOShared",
        "USOPrivate", "SoftwareDistribution", "Comms", "ConnectedDevicesPlatform", "D3DSCache",
        "PlaceholderTileLogoFolder", "Publishers", "PeerDistRepub", "VirtualStore", "CrashDumps",
        "IsolatedStorage", "History", "Temporary Internet Files", "MicrosoftEdge", "YrClean",
        "NVIDIA", "NVIDIA Corporation", "Intel", "AMD", "ATI", "Realtek"
    };

    public static List<Leftover> Scan(CancellationToken cancellation = default)
    {
        var leftovers = new List<Leftover>();
        leftovers.AddRange(OrphanFolders(cancellation));
        leftovers.AddRange(OrphanServices());
        leftovers.AddRange(OrphanTasks());
        leftovers.AddRange(OrphanShortcuts());
        return leftovers;
    }

    // Moves a leftover folder's files to quarantine through the normal safe-delete path:
    // the minimum age, protected file types and exclusions all still apply.
    public static DeleteResult QuarantineFolder(string folder, CleanSettings settings)
    {
        var files = new FolderScanner().Scan(folder, settings.ExcludedPaths).Select(f => f.FullPath).ToList();
        var result = SafeDeleteService.DeleteFiles(files, new[] { folder }, settings.MinAgeDays, settings.ExcludedPaths);
        CleanupLog.Write("leftover", result);
        return result;
    }

    // A folder is "known" when its name overlaps with an installed program's name, publisher or
    // install folder. Short or ambiguous names count as known - a miss is better than a false alarm.
    public static bool IsKnownFolder(string folderName, IEnumerable<string> installedTokens)
    {
        var name = Normalize(folderName);
        return name.Length < 3 || installedTokens.Any(token => token.Contains(name) || name.Contains(token));
    }

    public static List<string> InstalledTokens(IEnumerable<InstalledProgram> programs) =>
        programs
            .SelectMany(p => new[]
            {
                p.Name,
                p.Publisher ?? "",
                string.IsNullOrWhiteSpace(p.InstallLocation) ? "" : Path.GetFileName(p.InstallLocation.TrimEnd('\\', '"'))
            })
            .Select(Normalize)
            .Where(token => token.Length >= 3)
            .Distinct()
            .ToList();

    private static string Normalize(string text) =>
        new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static IEnumerable<Leftover> OrphanFolders(CancellationToken cancellation)
    {
        var tokens = InstalledTokens(InstalledPrograms.All);
        var cutoff = DateTime.Now.AddDays(-InactiveDays);
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
        };

        foreach (var root in roots.Where(Directory.Exists))
        {
            IEnumerable<DirectoryInfo> folders;
            try
            {
                folders = new DirectoryInfo(root).GetDirectories();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var folder in folders)
            {
                cancellation.ThrowIfCancellationRequested();
                if (folder.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                    folder.Attributes.HasFlag(FileAttributes.System) ||
                    SystemFolders.Contains(folder.Name) ||
                    ProtectedLocations.IsForbiddenRoot(folder.FullName) ||
                    IsKnownFolder(folder.Name, tokens))
                {
                    continue;
                }

                // Anything touched recently is evidently still in use by something
                if (folder.LastWriteTime > cutoff)
                    continue;

                var files = new FolderScanner().Scan(folder.FullName);
                if (files.Count == 0 || files.Any(f => File.GetLastWriteTime(f.FullPath) > cutoff))
                    continue;

                yield return new Leftover(LeftoverKind.Folder, folder.Name,
                    folder.FullName, null,
                    folder.FullName, files.Sum(f => f.SizeBytes));
            }
        }
    }

    // Win32 services whose executable no longer exists. Drivers are not examined.
    private static IEnumerable<Leftover> OrphanServices()
    {
        const int win32ServiceTypes = 0x10 | 0x20;
        var found = new List<Leftover>();
        try
        {
            using var services = Registry.LocalMachine.OpenSubKey(ServicesKey);
            foreach (var name in services?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var service = services!.OpenSubKey(name);
                if (service?.GetValue("Type") is not int type || (type & win32ServiceTypes) == 0 ||
                    service.GetValue("ImagePath") is not string imagePath)
                {
                    continue;
                }

                var executable = StartupManager.ExecutableFromCommand(imagePath);
                if (executable is not null && !File.Exists(executable))
                {
                    found.Add(new Leftover(LeftoverKind.Service, service.GetValue("DisplayName") as string ?? name,
                        name, executable, null, 0));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }

        return found;
    }

    // Third-party scheduled tasks pointing at a program that no longer exists.
    private static IEnumerable<Leftover> OrphanTasks()
    {
        var tasksRoot = Path.Combine(Environment.SystemDirectory, "Tasks");
        if (!Directory.Exists(tasksRoot))
            yield break;

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var file in Directory.EnumerateFiles(tasksRoot, "*", options))
        {
            var taskName = "\\" + Path.GetRelativePath(tasksRoot, file);
            if (taskName.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase))
                continue;

            string? executable = null;
            try
            {
                if (StartupManager.ParseTaskAction(File.ReadAllText(file)) is { } task)
                    executable = StartupManager.ExecutableFromCommand(task.Command);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
            }

            if (executable is not null && !File.Exists(executable))
                yield return new Leftover(LeftoverKind.Task, Path.GetFileName(file), taskName, executable, null, 0);
        }
    }

    // Start menu and desktop shortcuts whose target is gone.
    private static IEnumerable<Leftover> OrphanShortcuts()
    {
        var folders = new[]
        {
            Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms,
            Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory
        }.Select(Environment.GetFolderPath).Where(Directory.Exists);

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var shortcut in folders.SelectMany(folder => Directory.EnumerateFiles(folder, "*.lnk", options)))
        {
            var target = StartupManager.ShortcutTarget(shortcut);
            // Only plain file targets on a local drive can be judged; shell and network targets are skipped
            if (target is { Length: > 3 } && target[1] == ':' && !File.Exists(target) && !Directory.Exists(target) &&
                Directory.Exists(Path.GetPathRoot(target)))
            {
                yield return new Leftover(LeftoverKind.Shortcut, Path.GetFileNameWithoutExtension(shortcut),
                    shortcut, target, shortcut, 0);
            }
        }
    }
}
