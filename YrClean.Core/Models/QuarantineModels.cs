namespace YrClean.Core.Models;

// One quarantined file: where it came from and the neutral name it is stored under.
public record QuarantineEntry(string OriginalPath, string StoredName, long SizeBytes);

// Everything one cleanup operation moved into quarantine.
public record QuarantineBatchInfo(string Id, DateTime CreatedUtc, List<QuarantineEntry> Entries)
{
    public long TotalBytes => Entries.Sum(e => e.SizeBytes);
}

public class RestoreResult
{
    public int RestoredCount { get; set; }
    public List<string> Errors { get; } = new();
}

// One line of journal.jsonl. BatchId is set when the operation can be undone through quarantine.
public record JournalEntry(
    DateTime Time, string Kind, int Items, long Bytes, int Skipped, int Errors,
    string? BatchId = null, string? Note = null);
