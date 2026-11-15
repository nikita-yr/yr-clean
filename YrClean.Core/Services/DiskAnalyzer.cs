using System.Security.Cryptography;

namespace YrClean.Core.Services;

public record SizedPath(string Path, long Bytes, DateTime Modified);

public record DiskAnalysis(List<SizedPath> Folders, List<SizedPath> Files);

// Read-only views of where disk space went. Nothing in this class deletes or moves anything.
public static class DiskAnalyzer
{
    private static readonly EnumerationOptions Recursive = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    private static readonly EnumerationOptions TopLevel = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public static string DownloadsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    // Sizes of the folders directly under root (largest first) and the largest files anywhere below it.
    public static DiskAnalysis Analyze(string root, int top = 30, CancellationToken cancellation = default)
    {
        var folders = new List<SizedPath>();
        var largest = new PriorityQueue<SizedPath, long>(); // smallest of the kept files on top

        void Consider(FileInfo file)
        {
            largest.Enqueue(new SizedPath(file.FullName, file.Length, file.LastWriteTime), file.Length);
            if (largest.Count > top)
                largest.Dequeue();
        }

        var rootInfo = new DirectoryInfo(root);
        foreach (var file in SafeEnumerate(() => rootInfo.EnumerateFiles("*", TopLevel)))
            Consider(file);

        foreach (var folder in SafeEnumerate(() => rootInfo.EnumerateDirectories("*", TopLevel)))
        {
            long total = 0;
            foreach (var file in SafeEnumerate(() => folder.EnumerateFiles("*", Recursive)))
            {
                cancellation.ThrowIfCancellationRequested();
                total += file.Length;
                Consider(file);
            }

            folders.Add(new SizedPath(folder.FullName, total, folder.LastWriteTime));
        }

        return new DiskAnalysis(
            folders.OrderByDescending(f => f.Bytes).Take(top).ToList(),
            largest.UnorderedItems.Select(i => i.Element).OrderByDescending(f => f.Bytes).ToList());
    }

    // Files not modified for at least olderThanDays, largest first.
    public static List<SizedPath> OldFiles(string folder, int olderThanDays)
    {
        var cutoff = DateTime.Now.AddDays(-olderThanDays);
        return SafeEnumerate(() => new DirectoryInfo(folder).EnumerateFiles("*", Recursive))
            .Where(f => f.LastWriteTime < cutoff)
            .Select(f => new SizedPath(f.FullName, f.Length, f.LastWriteTime))
            .OrderByDescending(f => f.Bytes)
            .ToList();
    }

    // Groups of byte-identical files, ordered by how much space the extra copies waste.
    // Files are compared by size first, so only same-sized candidates are ever read and hashed.
    public static List<List<SizedPath>> FindDuplicates(string folder, long minBytes = 1024 * 1024, CancellationToken cancellation = default)
    {
        var sameSize = SafeEnumerate(() => new DirectoryInfo(folder).EnumerateFiles("*", Recursive))
            .Where(f => f.Length >= minBytes)
            .GroupBy(f => f.Length)
            .Where(g => g.Count() > 1);

        var groups = new List<List<SizedPath>>();
        foreach (var candidates in sameSize)
        {
            var byHash = new Dictionary<string, List<SizedPath>>();
            foreach (var file in candidates)
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    using var stream = file.OpenRead();
                    var hash = Convert.ToHexString(SHA256.HashData(stream));
                    if (!byHash.TryGetValue(hash, out var list))
                        byHash[hash] = list = new List<SizedPath>();
                    list.Add(new SizedPath(file.FullName, file.Length, file.LastWriteTime));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Locked or unreadable files are simply not compared
                }
            }

            groups.AddRange(byHash.Values.Where(g => g.Count > 1));
        }

        return groups.OrderByDescending(g => g[0].Bytes * (g.Count - 1)).ToList();
    }

    // Drives whose free space is below the threshold percentage.
    public static List<DriveSpace> LowSpaceDrives(int thresholdPercent) =>
        SystemHealth.GetDriveSpace().Where(d => d.FreePercent < thresholdPercent).ToList();

    public static string LowSpaceMessage(IEnumerable<DriveSpace> drives) => string.Join("; ", drives.Select(d =>
        $"{d.Name} has {SizeFormatter.Format(d.FreeBytes)} free ({d.FreePercent:F0}%)"));

    // Enumeration can still fail on the starting folder itself, or mid-way on an odd volume
    private static IEnumerable<T> SafeEnumerate<T>(Func<IEnumerable<T>> source)
    {
        IEnumerator<T> enumerator;
        try
        {
            enumerator = source().GetEnumerator();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        using (enumerator)
        {
            while (true)
            {
                try
                {
                    if (!enumerator.MoveNext())
                        yield break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    yield break;
                }

                yield return enumerator.Current;
            }
        }
    }
}
