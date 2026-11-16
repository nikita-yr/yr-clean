using System.Globalization;
using System.Text.Json;
using YrClean.Core.Models;

namespace YrClean.Core.Services;

// Holds "deleted" files for a grace period so any cleanup can be undone.
// Layout: <root>\<batch id>\manifest.jsonl plus the files under neutral names (0.q, 1.q, ...).
public sealed class QuarantineService
{
    private const string ManifestName = "manifest.jsonl";
    private const string StoredExtension = ".q";
    private const string IdTimeFormat = "yyyyMMdd-HHmmss";

    public static QuarantineService Default { get; } = new(AppPaths.QuarantineDirectory);

    public QuarantineService(string root) => Root = root;

    public string Root { get; }

    public Batch BeginBatch() => new(Root);

    public sealed class Batch : IDisposable
    {
        private readonly string _directory;
        private StreamWriter? _manifest;
        private int _nextIndex;

        internal Batch(string root)
        {
            Id = DateTime.UtcNow.ToString(IdTimeFormat, CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8];
            _directory = Path.Combine(root, Id);
        }

        public string Id { get; }
        public bool IsEmpty => _nextIndex == 0;

        // Moves the file into the batch. The manifest line is written first, so a crash can
        // never leave a stored file whose original location is unknown.
        public void Add(FileInfo file)
        {
            if (_manifest is null)
            {
                Directory.CreateDirectory(_directory);
                _manifest = new StreamWriter(Path.Combine(_directory, ManifestName)) { AutoFlush = true };
            }

            var storedName = _nextIndex++ + StoredExtension;
            _manifest.WriteLine(JsonSerializer.Serialize(new QuarantineEntry(file.FullName, storedName, file.Length)));
            File.Move(file.FullName, Path.Combine(_directory, storedName));
        }

        public void Dispose() => _manifest?.Dispose();
    }

    // Batches that still hold at least one file, newest first.
    public List<QuarantineBatchInfo> ListBatches()
    {
        var batches = new List<QuarantineBatchInfo>();
        foreach (var directory in BatchDirectories())
        {
            var entries = ReadEntries(directory.Path)
                .Where(e => File.Exists(Path.Combine(directory.Path, e.StoredName)))
                .ToList();
            if (entries.Count > 0)
                batches.Add(new QuarantineBatchInfo(Path.GetFileName(directory.Path), directory.CreatedUtc, entries));
        }

        return batches.OrderByDescending(b => b.CreatedUtc).ToList();
    }

    // Moves the chosen files back to where they came from. The manifest lives in a user-writable
    // folder while the app runs elevated, so every destination is re-validated like a deletion target.
    public RestoreResult Restore(string batchId, IEnumerable<string> storedNames)
    {
        var result = new RestoreResult();
        if (batchId != Path.GetFileName(batchId))
            throw new ArgumentException("Invalid quarantine batch id.", nameof(batchId));

        var directory = Path.Combine(Root, batchId);
        var wanted = storedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in ReadEntries(directory).Where(e => wanted.Contains(e.StoredName)))
        {
            var source = Path.Combine(directory, entry.StoredName);
            try
            {
                if (!File.Exists(source))
                    continue;

                var destination = ProtectedLocations.Normalize(entry.OriginalPath);
                if (SafeDeleteService.RestoreBlockReason(destination) is { } reason)
                {
                    result.Errors.Add($"{reason}: {destination}");
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(source, destination); // throws rather than overwrite if something reappeared
                result.RestoredCount++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{entry.OriginalPath}: {ex.Message}");
            }
        }

        if (Directory.Exists(directory) && !Directory.EnumerateFiles(directory, "*" + StoredExtension).Any())
            TryDeleteBatch(directory);

        return result;
    }

    // Permanently deletes batches older than the retention period. Returns how many were removed.
    // This is the only place files are really deleted; it never leaves the quarantine root.
    public int Purge(int olderThanDays, DateTime? nowUtc = null)
    {
        var cutoff = (nowUtc ?? DateTime.UtcNow).AddDays(-olderThanDays);
        return BatchDirectories().Where(d => d.CreatedUtc < cutoff).Count(d => TryDeleteBatch(d.Path));
    }

    // Only folders this service created (timestamped name, not a link) count as batches.
    private IEnumerable<(string Path, DateTime CreatedUtc)> BatchDirectories()
    {
        if (!Directory.Exists(Root))
            yield break;

        foreach (var directory in Directory.GetDirectories(Root))
        {
            var name = Path.GetFileName(directory);
            if (name.Length > IdTimeFormat.Length &&
                DateTime.TryParseExact(name[..IdTimeFormat.Length], IdTimeFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var created) &&
                !new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                yield return (directory, created);
            }
        }
    }

    private static List<QuarantineEntry> ReadEntries(string batchDirectory)
    {
        var entries = new List<QuarantineEntry>();
        var manifest = Path.Combine(batchDirectory, ManifestName);
        if (!File.Exists(manifest))
            return entries;

        foreach (var line in File.ReadLines(manifest))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<QuarantineEntry>(line);
                // A stored name must be a plain "<n>.q" file name, never a path out of the batch
                if (entry is { OriginalPath.Length: > 0 } &&
                    entry.StoredName == Path.GetFileName(entry.StoredName) &&
                    entry.StoredName.EndsWith(StoredExtension, StringComparison.OrdinalIgnoreCase))
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
                // A torn last line after a crash; the other entries are still good
            }
        }

        return entries;
    }

    private static bool TryDeleteBatch(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // retried on the next purge
        }
    }
}
