namespace YrClean.Core.Services;

// Resolves paths that may contain a single "*" wildcard segment,
// e.g. "C:\Users\me\AppData\Local\Mozilla\Firefox\Profiles\*\cache2\entries"
// into all matching real paths (since profile folder names are random per install).
public static class PathWildcardResolver
{
    public static List<string> Resolve(string pathWithWildcards)
    {
        var segments = pathWithWildcards.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var currentBases = new List<string> { segments[0] + Path.DirectorySeparatorChar };

        for (int i = 1; i < segments.Length; i++)
        {
            var segment = segments[i];
            var nextBases = new List<string>();

            foreach (var basePath in currentBases)
            {
                if (segment.Contains('*'))
                {
                    if (!Directory.Exists(basePath))
                        continue;

                    var matches = Directory.GetDirectories(basePath, segment);
                    nextBases.AddRange(matches);
                }
                else
                {
                    nextBases.Add(Path.Combine(basePath, segment));
                }
            }

            currentBases = nextBases;
        }

        return currentBases.Where(Directory.Exists).ToList();
    }
}