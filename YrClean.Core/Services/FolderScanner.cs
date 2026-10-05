using YrClean.Core.Models;

namespace YrClean.Core.Services;

public class FolderScanner
{
    public List<CacheEntry> Scan(string rootPath)
    {
        var results = new List<CacheEntry>();

        if (!Directory.Exists(rootPath))
            return results;

        try
        {
            var pendingDirectories = new Stack<string>();
            var rootInfo = new DirectoryInfo(rootPath);
            if (rootInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return results;

            pendingDirectories.Push(rootInfo.FullName);
            while (pendingDirectories.Count > 0)
            {
                var directory = pendingDirectories.Pop();

                foreach (var filePath in Directory.EnumerateFiles(directory))
                {
                    try
                    {
                        var info = new FileInfo(filePath);
                        if (info.Length == 0)
                            continue;

                        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                            continue;

                        results.Add(new CacheEntry
                        {
                            FullPath = info.FullName,
                            SizeBytes = info.Length,
                            LastAccessTime = info.LastAccessTime
                        });
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }

                foreach (var subdirectory in Directory.EnumerateDirectories(directory))
                {
                    try
                    {
                        var info = new DirectoryInfo(subdirectory);
                        if (!info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                            pendingDirectories.Push(info.FullName);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // An inaccessible subdirectory should not abort an unattended scan
        }
        catch (IOException)
        {
            // A transient filesystem error should not abort an unattended scan
        }

        return results;
    }

    // Groups files by their top-level subfolder under rootPath, sorted by size descending
    public List<CacheGroup> ScanGrouped(string rootPath)
    {
        var entries = Scan(rootPath);
        var groups = new Dictionary<string, CacheGroup>();

        foreach (var entry in entries)
        {
            string groupKey = GetTopLevelFolder(rootPath, entry.FullPath);

            if (!groups.TryGetValue(groupKey, out var group))
            {
                group = new CacheGroup
                {
                    FolderPath = groupKey,
                    FullFolderPath = groupKey == "(root)" ? rootPath : Path.Combine(rootPath, groupKey)
                };
                groups[groupKey] = group;
            }

            group.TotalSizeBytes += entry.SizeBytes;
            group.FileCount += 1;
            group.Files.Add(entry);
        }

        return groups.Values
            .OrderByDescending(g => g.TotalSizeBytes)
            .ToList();
    }

    // Returns the immediate subfolder name under root, or "(root)" if the file sits directly in root
    private string GetTopLevelFolder(string rootPath, string fullFilePath)
    {
        var relative = Path.GetRelativePath(rootPath, fullFilePath);
        var firstPart = relative.Split(Path.DirectorySeparatorChar)[0];

        return firstPart == Path.GetFileName(fullFilePath) ? "(root)" : firstPart;
    }
}