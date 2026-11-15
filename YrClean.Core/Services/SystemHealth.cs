using System.Text;
using System.Text.Json;

namespace YrClean.Core.Services;

public record DriveSpace(string Name, long TotalBytes, long FreeBytes)
{
    public double FreePercent => TotalBytes == 0 ? 0 : 100.0 * FreeBytes / TotalBytes;
}

// Status follows MSFT_PhysicalDisk.HealthStatus: 0 healthy, 1 warning, 2 unhealthy, anything else unknown.
public record DiskHealth(string Name, int Status)
{
    public bool NeedsAttention => Status is 1 or 2;
    public string StatusText => Status switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "Unknown" };
}

public static class SystemHealth
{
    // "Check system": repairs the component store, then verifies system files against it.
    public static List<ToolResult> CheckSystem(Action<string> log, CancellationToken cancellation = default)
    {
        var results = new List<ToolResult>
        {
            SystemMaintenance.RunTool("dism-restore-health", "dism.exe",
                new[] { "/Online", "/Cleanup-Image", "/RestoreHealth" }, log, null, cancellation)
        };

        // sfc writes UTF-16 when its output is redirected
        results.Add(SystemMaintenance.RunTool("sfc-scannow", "sfc.exe", new[] { "/scannow" }, log, Encoding.Unicode, cancellation));
        return results;
    }

    public static List<DriveSpace> GetDriveSpace() =>
        DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => new DriveSpace(d.Name, d.TotalSize, d.AvailableFreeSpace))
            .ToList();

    // SMART-based health as Windows reports it. Empty when the storage provider is unavailable.
    public static List<DiskHealth> GetDiskHealth()
    {
        var output = new StringBuilder();
        const string command =
            "Get-CimInstance -Namespace root/Microsoft/Windows/Storage -ClassName MSFT_PhysicalDisk | " +
            "Select-Object FriendlyName,HealthStatus | ConvertTo-Json -Compress";
        try
        {
            ProcessRunner.Run(
                Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                new[] { "-NoProfile", "-NonInteractive", "-Command", "[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + command },
                line => output.AppendLine(line),
                Encoding.UTF8);
            return ParseDiskHealth(output.ToString());
        }
        catch (Exception)
        {
            return new List<DiskHealth>();
        }
    }

    // PowerShell emits a bare object for one disk and an array for several
    public static List<DiskHealth> ParseDiskHealth(string json)
    {
        var disks = new List<DiskHealth>();
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            foreach (var disk in root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToList() : new List<JsonElement> { root })
            {
                disks.Add(new DiskHealth(
                    disk.GetProperty("FriendlyName").GetString() ?? "Disk",
                    disk.GetProperty("HealthStatus").TryGetInt32(out var status) ? status : -1));
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // No usable answer means "unknown", which the caller shows as an empty list
        }

        return disks;
    }
}
