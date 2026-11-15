using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Win32;
using YrClean.Core.Models;

namespace YrClean.Core.Services;

public enum StartupKind
{
    RegistryRun,
    StartupFolder,
    ScheduledTask
}

public class StartupItem
{
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public string Location { get; init; } = "";
    public StartupKind Kind { get; init; }
    public bool Enabled { get; set; }

    // Null when the command does not name a full path we could check
    public string? ExecutablePath { get; init; }
    public bool FileMissing { get; init; }
    public string? Publisher { get; init; }
    public bool UnknownPublisher => !FileMissing && string.IsNullOrWhiteSpace(Publisher);

    // Where the enabled/disabled switch lives: a StartupApproved value, or the task name
    internal RegistryHive ApprovedHive { get; init; }
    internal string ApprovedSubKey { get; init; } = "";
    internal string SwitchName { get; init; } = "";
}

// Lists what starts with Windows and turns entries off or back on. Nothing is ever deleted:
// registry and Startup-folder entries are switched the same way Task Manager does it (the
// StartupApproved keys), scheduled tasks through "schtasks /Change".
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    public static List<StartupItem> GetItems()
    {
        var items = new List<StartupItem>();

        AddRunKey(items, RegistryHive.CurrentUser, RegistryView.Default, "Run", @"HKCU\...\Run");
        AddRunKey(items, RegistryHive.LocalMachine, RegistryView.Registry64, "Run", @"HKLM\...\Run");
        AddRunKey(items, RegistryHive.LocalMachine, RegistryView.Registry32, "Run32", @"HKLM\...\Run (32-bit)");

        AddStartupFolder(items, Environment.SpecialFolder.Startup, RegistryHive.CurrentUser);
        AddStartupFolder(items, Environment.SpecialFolder.CommonStartup, RegistryHive.LocalMachine);

        AddScheduledTasks(items);
        return items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static bool SetEnabled(StartupItem item, bool enabled)
    {
        bool ok;
        try
        {
            if (item.Kind == StartupKind.ScheduledTask)
            {
                ok = ProcessRunner.Run(ProcessRunner.System32("schtasks.exe"),
                    new[] { "/Change", "/TN", item.SwitchName, enabled ? "/ENABLE" : "/DISABLE" }) == 0;
            }
            else
            {
                using var root = RegistryKey.OpenBaseKey(item.ApprovedHive, RegistryView.Registry64);
                using var key = root.CreateSubKey($@"{ApprovedKey}\{item.ApprovedSubKey}");
                key.SetValue(item.SwitchName, ApprovedValue(enabled, DateTime.UtcNow), RegistryValueKind.Binary);
                ok = true;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            ok = false;
        }

        if (ok)
            item.Enabled = enabled;

        OperationJournal.Append(new JournalEntry(
            DateTime.Now, enabled ? "startup-enable" : "startup-disable", 1, 0, 0, ok ? 0 : 1, Note: item.Name));
        return ok;
    }

    // StartupApproved format: 12 bytes; an even first byte (2, 6) means enabled, an odd one (3, 7)
    // disabled, followed by the time it was disabled. No value at all means enabled.
    public static bool IsApproved(byte[]? value) => value is not { Length: > 0 } || value[0] % 2 == 0;

    public static byte[] ApprovedValue(bool enabled, DateTime utcNow)
    {
        var value = new byte[12];
        value[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled)
            BitConverter.GetBytes(utcNow.ToFileTimeUtc()).CopyTo(value, 4);
        return value;
    }

    // Best-effort extraction of the program from a command line such as
    // "C:\Program Files\App\app.exe" --minimized   or   C:\Tools\app.exe /background
    public static string? ExecutableFromCommand(string command)
    {
        var text = Environment.ExpandEnvironmentVariables(command.Trim());
        string candidate;

        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            candidate = end > 0 ? text[1..end] : text[1..];
        }
        else
        {
            var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            candidate = exe > 0 ? text[..(exe + 4)] : text.Split(' ')[0];
        }

        return Path.IsPathFullyQualified(candidate) ? candidate : null;
    }

    private static void AddRunKey(List<StartupItem> items, RegistryHive hive, RegistryView view, string approvedSubKey, string location)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var run = root.OpenSubKey(RunKey);
            if (run is null)
                return;

            using var approvedRoot = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var approved = approvedRoot.OpenSubKey($@"{ApprovedKey}\{approvedSubKey}");

            foreach (var name in run.GetValueNames().Where(n => n.Length > 0))
            {
                var command = run.GetValue(name)?.ToString() ?? "";
                items.Add(Describe(name, command, location, StartupKind.RegistryRun,
                    IsApproved(approved?.GetValue(name) as byte[]), ExecutableFromCommand(command),
                    hive, approvedSubKey, name));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // An unreadable hive simply contributes no entries
        }
    }

    private static void AddStartupFolder(List<StartupItem> items, Environment.SpecialFolder folder, RegistryHive hive)
    {
        try
        {
            var path = Environment.GetFolderPath(folder);
            if (path.Length == 0 || !Directory.Exists(path))
                return;

            using var approvedRoot = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var approved = approvedRoot.OpenSubKey($@"{ApprovedKey}\StartupFolder");

            foreach (var file in Directory.GetFiles(path))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                    continue;

                var target = Path.GetExtension(file).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                    ? ShortcutTarget(file)
                    : file;
                items.Add(Describe(Path.GetFileNameWithoutExtension(file), target ?? file, path, StartupKind.StartupFolder,
                    IsApproved(approved?.GetValue(fileName) as byte[]), target, hive, "StartupFolder", fileName));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
        }
    }

    // Third-party tasks that fire at logon or boot. Windows' own tasks (\Microsoft\...) are left out.
    private static void AddScheduledTasks(List<StartupItem> items)
    {
        var tasksRoot = Path.Combine(Environment.SystemDirectory, "Tasks");
        if (!Directory.Exists(tasksRoot))
            return;

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var file in Directory.EnumerateFiles(tasksRoot, "*", options))
        {
            var taskName = "\\" + Path.GetRelativePath(tasksRoot, file);
            if (taskName.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                if (ParseTask(File.ReadAllText(file)) is { } task)
                {
                    items.Add(Describe(Path.GetFileName(file), task.Command, taskName,
                        StartupKind.ScheduledTask, task.Enabled, ExecutableFromCommand(task.Command),
                        default, "", taskName));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
            }
        }
    }

    // Returns the task's command if it has a logon or boot trigger, otherwise null.
    public static (string Command, bool Enabled)? ParseTask(string xml) =>
        ParseTaskAction(xml) is { RunsAtStartup: true } task ? (task.Command, task.Enabled) : null;

    // The program a task runs, whatever its trigger; null when it has no Exec action.
    public static (string Command, bool Enabled, bool RunsAtStartup)? ParseTaskAction(string xml)
    {
        var task = XDocument.Parse(xml).Root;
        if (task is null)
            return null;

        XNamespace ns = task.Name.Namespace;
        var triggers = task.Element(ns + "Triggers");
        var runsAtStartup = triggers?.Element(ns + "LogonTrigger") is not null || triggers?.Element(ns + "BootTrigger") is not null;

        var exec = task.Element(ns + "Actions")?.Element(ns + "Exec");
        if (exec is null)
            return null;

        var program = exec.Element(ns + "Command")?.Value.Trim() ?? "";
        var arguments = exec.Element(ns + "Arguments")?.Value.Trim() ?? "";
        var command = (program.Contains(' ') && !program.StartsWith('"') ? $"\"{program}\"" : program) +
                      (arguments.Length > 0 ? " " + arguments : "");

        var enabled = !string.Equals(task.Element(ns + "Settings")?.Element(ns + "Enabled")?.Value, "false", StringComparison.OrdinalIgnoreCase);
        return (command, enabled, runsAtStartup);
    }

    private static StartupItem Describe(
        string name, string command, string location, StartupKind kind, bool enabled, string? executable,
        RegistryHive approvedHive, string approvedSubKey, string switchName)
    {
        var missing = executable is not null && !File.Exists(executable);
        string? publisher = null;
        if (executable is not null && !missing)
        {
            try
            {
                // shortcut: the publisher is the file's own version resource, not a verified signature;
                // upgrade to an Authenticode check if this is ever used for trust decisions
                publisher = FileVersionInfo.GetVersionInfo(executable).CompanyName;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return new StartupItem
        {
            Name = name,
            Command = command,
            Location = location,
            Kind = kind,
            Enabled = enabled,
            ExecutablePath = executable,
            FileMissing = missing,
            Publisher = publisher,
            ApprovedHive = approvedHive,
            ApprovedSubKey = approvedSubKey,
            SwitchName = switchName
        };
    }

    internal static string? ShortcutTarget(string shortcutPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
                return null;

            dynamic shell = Activator.CreateInstance(shellType)!;
            string target = shell.CreateShortcut(shortcutPath).TargetPath;
            return string.IsNullOrEmpty(target) ? null : target;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
