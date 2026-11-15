using System.Text.Json;
using YrClean.Core.Models;

namespace YrClean.Core.Services;

// Machine-readable history of every operation, one JSON object per line.
public static class OperationJournal
{
    private static readonly string JournalPath = Path.Combine(AppPaths.DataDirectory, "journal.jsonl");

    public static void Append(JournalEntry entry)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.AppendAllLines(JournalPath, new[] { JsonSerializer.Serialize(entry) });
        }
        catch (Exception)
        {
            // Journaling must never crash an operation
        }
    }

    // Newest first. Unreadable lines are skipped.
    public static List<JournalEntry> Read()
    {
        var entries = new List<JournalEntry>();
        try
        {
            foreach (var line in File.ReadLines(JournalPath))
            {
                try
                {
                    if (JsonSerializer.Deserialize<JournalEntry>(line) is { } entry)
                        entries.Add(entry);
                }
                catch (JsonException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No journal yet
        }

        entries.Reverse();
        return entries;
    }
}
