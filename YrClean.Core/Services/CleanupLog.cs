using YrClean.Core.Models;

namespace YrClean.Core.Services;

// Appends one line per cleanup run (plus its errors) to autoclean.log, so headless runs can be audited.
public static class CleanupLog
{
    private static readonly string LogPath = Path.Combine(AppPaths.DataDirectory, "autoclean.log");

    public static void Write(string runKind, DeleteResult result)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);

            var lines = new List<string>
            {
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  run={runKind}  deleted={result.DeletedCount}  " +
                $"freed={SizeFormatter.Format(result.FreedBytes)}  skipped={result.SkippedCount}  " +
                $"errors={result.Errors.Count}"
            };
            lines.AddRange(result.Errors.Select(error => "    " + error));

            File.AppendAllLines(LogPath, lines);
        }
        catch (Exception)
        {
            // Logging must never crash a cleanup
        }
    }
}
