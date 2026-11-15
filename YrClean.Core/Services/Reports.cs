using YrClean.Core.Models;

namespace YrClean.Core.Services;

// Plain-text reports for the command-line switches. Always English, like the log.
public static class Reports
{
    public static readonly string Directory = Path.Combine(AppPaths.DataDirectory, "reports");

    public static List<string> Startup()
    {
        var items = StartupManager.GetItems();
        var lines = new List<string> { $"Startup entries: {items.Count} ({items.Count(i => i.Enabled)} enabled)", "" };
        foreach (var item in items)
        {
            var flags = new[]
            {
                item.Enabled ? "enabled" : "disabled",
                item.FileMissing ? "EXECUTABLE NOT FOUND" : null,
                item.UnknownPublisher && item.ExecutablePath is not null ? "unknown publisher" : null
            }.Where(f => f is not null);

            lines.Add($"{item.Name}  [{string.Join(", ", flags)}]");
            lines.Add($"    {item.Location}");
            lines.Add($"    {item.Command}");
        }

        return lines;
    }

    public static List<string> Disk(CleanSettings settings, int top = 15)
    {
        var lines = new List<string> { "Free space:" };
        foreach (var drive in SystemHealth.GetDriveSpace())
        {
            lines.Add($"    {drive.Name}  {SizeFormatter.Format(drive.FreeBytes)} free of {SizeFormatter.Format(drive.TotalBytes)} " +
                      $"({drive.FreePercent:F0}%)" +
                      (drive.FreePercent < settings.LowSpaceThresholdPercent ? $"  LOW (below {settings.LowSpaceThresholdPercent}%)" : ""));
        }

        var disks = SystemHealth.GetDiskHealth();
        lines.Add("");
        lines.Add("Disk health:" + (disks.Count == 0 ? " not available" : ""));
        lines.AddRange(disks.Select(d => $"    {d.Name}: {d.StatusText}"));

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var analysis = DiskAnalyzer.Analyze(profile, top);
        lines.Add("");
        lines.Add($"Largest folders in {profile}:");
        lines.AddRange(analysis.Folders.Select(f => $"    {SizeFormatter.Format(f.Bytes),12}  {f.Path}"));
        lines.Add("");
        lines.Add("Largest files:");
        lines.AddRange(analysis.Files.Select(f => $"    {SizeFormatter.Format(f.Bytes),12}  {f.Path}"));
        return lines;
    }

    public static List<string> Verdicts(IEnumerable<ToolResult> results) =>
        results.Select(r => $"{r.Tool}: {(r.Succeeded ? "completed" : $"exit code {r.ExitCode}")} - {r.Verdict}").ToList();

    // Writes the report next to the log and returns its path.
    public static string Save(string name, IEnumerable<string> lines)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = Path.Combine(Directory, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllLines(path, lines);
        return path;
    }
}
