using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using YrClean.Core.Models;

namespace YrClean.Core.Services;

// Outcome of one of Windows' own tools: its exit code and the last things it said.
public record ToolResult(string Tool, int ExitCode, string Verdict)
{
    public bool Succeeded => ExitCode == 0;
}

// Cleanup and repair operations that Windows itself performs. Except for the update cache, which
// goes through SafeDeleteService like any other folder, these cannot be undone via quarantine -
// the UI asks for confirmation and creates a restore point first.
public static class SystemMaintenance
{
    private const string WindowsOldHandlerKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches\Previous Installations";
    private const int SageSet = 777;
    private static readonly string SageValueName = $"StateFlags{SageSet:D4}";

    public static string WindowsOldPath =>
        Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "Windows.old");

    public static bool WindowsOldExists => Directory.Exists(WindowsOldPath);

    // Runs a tool, echoing its output, and keeps its last lines as the human-readable verdict.
    public static ToolResult RunTool(
        string name, string exe, string[] args, Action<string> log, Encoding? encoding = null,
        CancellationToken cancellation = default)
    {
        var tail = new Queue<string>();
        log($"> {name} {string.Join(' ', args)}");

        var exitCode = ProcessRunner.Run(ProcessRunner.System32(exe), args, line =>
        {
            log(line);
            if (ProcessRunner.TryParsePercent(line) is null)
            {
                tail.Enqueue(line);
                if (tail.Count > 3)
                    tail.Dequeue();
            }
        }, encoding, cancellation);

        var result = new ToolResult(name, exitCode, string.Join(" ", tail));
        OperationJournal.Append(new JournalEntry(DateTime.Now, name, 0, 0, 0, exitCode == 0 ? 0 : 1, Note: $"exit code {exitCode}"));
        CleanupLog.WriteLine($"run={name}  exit={exitCode}");
        return result;
    }

    public static ToolResult ComponentCleanup(Action<string> log, CancellationToken cancellation = default) =>
        RunTool("dism-component-cleanup", "dism.exe",
            new[] { "/Online", "/Cleanup-Image", "/StartComponentCleanup" }, log, null, cancellation);

    // A System Restore checkpoint. Returns false when System Restore is off or refuses;
    // Windows itself silently skips the request if a point was made in the last 24 hours.
    public static bool CreateRestorePoint(string description, Action<string> log)
    {
        var powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        var command = $"Checkpoint-Computer -Description '{description.Replace("'", "")}' " +
                      "-RestorePointType MODIFY_SETTINGS -ErrorAction Stop";
        try
        {
            log("> Creating a restore point...");
            var exitCode = ProcessRunner.Run(powershell,
                new[] { "-NoProfile", "-NonInteractive", "-Command", command }, log);
            return exitCode == 0;
        }
        catch (Exception ex)
        {
            log(ex.Message);
            return false;
        }
    }

    // Empties SoftwareDistribution\Download with the Windows Update service stopped, then puts
    // the service back the way it was. Files still pass every SafeDeleteService check and land
    // in quarantine, so recent downloads (pending updates) and executables are left in place.
    public static DeleteResult ClearUpdateCache(CleanSettings settings, Action<string> log)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"SoftwareDistribution\Download");
        var wasRunning = IsServiceRunning("wuauserv");

        try
        {
            if (wasRunning)
                RunNet("stop", log);

            var files = new FolderScanner().Scan(root, settings.ExcludedPaths).Select(f => f.FullPath).ToList();
            var result = SafeDeleteService.DeleteFiles(files, new[] { root }, settings.MinAgeDays, settings.ExcludedPaths);
            CleanupLog.Write("update-cache", result);
            return result;
        }
        finally
        {
            if (wasRunning)
                RunNet("start", log);
        }
    }

    private static void RunNet(string verb, Action<string> log)
    {
        log($"> net {verb} wuauserv");
        ProcessRunner.Run(ProcessRunner.System32("net.exe"), new[] { verb, "wuauserv" }, log);
    }

    private static bool IsServiceRunning(string service)
    {
        var running = false;
        // The state number is not localized: "STATE : 4  RUNNING"
        ProcessRunner.Run(ProcessRunner.System32("sc.exe"), new[] { "query", service },
            line => running |= System.Text.RegularExpressions.Regex.IsMatch(line, @":\s*4\s+RUNNING"));
        return running;
    }

    // Removes the previous Windows installation the supported way: Disk Cleanup's own
    // "Previous Installations" handler, selected through a temporary /sagerun profile.
    // The profile flag is the only registry value written, and it is removed afterwards.
    public static ToolResult RemoveWindowsOld(Action<string> log, CancellationToken cancellation = default)
    {
        using var handler = Registry.LocalMachine.OpenSubKey(WindowsOldHandlerKey, writable: true)
            ?? throw new InvalidOperationException("Disk Cleanup has no 'Previous Installations' handler on this system.");

        handler.SetValue(SageValueName, 2, RegistryValueKind.DWord);
        try
        {
            var result = RunTool("remove-windows-old", "cleanmgr.exe", new[] { $"/sagerun:{SageSet}" }, log, null, cancellation);
            return WindowsOldExists
                ? result with { ExitCode = result.ExitCode == 0 ? 1 : result.ExitCode, Verdict = "Windows.old is still present." }
                : result with { Verdict = "Windows.old was removed." };
        }
        finally
        {
            handler.DeleteValue(SageValueName, throwOnMissingValue: false);
        }
    }

    public static (long Bytes, long Items) QueryRecycleBin()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? (info.i64Size, info.i64NumItems) : (0, 0);
    }

    // Permanent by nature: the recycle bin is already the user's own "quarantine".
    public static bool EmptyRecycleBin()
    {
        var (bytes, items) = QueryRecycleBin();
        if (items == 0)
            return true;

        const uint noConfirmationNoProgressNoSound = 0x1 | 0x2 | 0x4;
        var ok = SHEmptyRecycleBin(IntPtr.Zero, null, noConfirmationNoProgressNoSound) == 0;
        OperationJournal.Append(new JournalEntry(DateTime.Now, "recycle-bin", (int)items, bytes, 0, ok ? 0 : 1));
        CleanupLog.WriteLine($"run=recycle-bin  items={items}  size={SizeFormatter.Format(bytes)}  ok={ok}");
        return ok;
    }

    // Default packing matches the 64-bit SDK layout; YrClean ships as x64 only
    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref SHQUERYRBINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);
}
