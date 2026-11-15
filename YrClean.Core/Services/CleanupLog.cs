using YrClean.Core.Models;

namespace YrClean.Core.Services;

// Appends one line per cleanup run (plus its errors) to autoclean.log, so headless runs can be audited.
public static class CleanupLog
{
    private static readonly string LogPath = Path.Combine(AppPaths.DataDirectory, "autoclean.log");

    public static void WriteLine(string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.AppendAllLines(LogPath, new[] { $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}" });
        }
        catch (Exception)
        {
            // Logging must never crash a cleanup
        }
    }

    public static void Write(string runKind, DeleteResult result)
    {
        OperationJournal.Append(new JournalEntry(
            DateTime.Now, runKind, result.DeletedCount, result.FreedBytes,
            result.SkippedCount, result.Errors.Count, result.QuarantineBatchId));

        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);

            var lines = new List<string>
            {
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  run={runKind}  quarantined={result.DeletedCount}  " +
                $"size={SizeFormatter.Format(result.FreedBytes)}  skipped={result.SkippedCount}  " +
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
