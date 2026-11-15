using System.Text.RegularExpressions;
using YrClean.Core.Models;

namespace YrClean.Core.Services;

// Imports the file-cleaning part of a winapp2.ini (the community rule format used by CCleaner
// and BleachBit). Registry entries (RegKey) are ignored on purpose: YrClean does not clean the
// registry. Every imported rule is Review level, so it is never run on a schedule.
public static class Winapp2Parser
{
    // winapp2 variables that are not real environment variables
    private static readonly (string Name, string Value)[] Variables =
    {
        ("%CommonAppData%", "%ProgramData%"),
        ("%LocalLowAppData%", @"%UserProfile%\AppData\LocalLow"),
        ("%Documents%", @"%UserProfile%\Documents"),
        ("%Pictures%", @"%UserProfile%\Pictures"),
        ("%Music%", @"%UserProfile%\Music"),
        ("%Video%", @"%UserProfile%\Videos"),
        ("%WinDir%", "%SystemRoot%")
    };

    public static List<CacheSource> Parse(string text)
    {
        var rules = new List<CacheSource>();
        CacheSource? current = null;

        void Finish()
        {
            if (current is { Paths.Count: > 0 })
                rules.Add(current);
        }

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == ';')
                continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                Finish();
                var name = line[1..^1].TrimEnd('*', ' ');
                current = new CacheSource
                {
                    Id = "winapp2." + Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-'),
                    Name = name,
                    Category = "winapp2",
                    Level = RuleLevel.Review
                };
                continue;
            }

            var equals = line.IndexOf('=');
            if (current is null || equals <= 0)
                continue;

            var key = line[..equals].TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            var value = line[(equals + 1)..].Trim();
            if (value.Length == 0)
                continue;

            switch (key.ToLowerInvariant())
            {
                case "section":
                    current.Category = value;
                    break;
                case "detect":
                case "detectfile":
                    current.Detect.Add(MapVariables(value));
                    break;
                case "filekey":
                    current.Paths.AddRange(ParseFileKey(value));
                    break;
                case "excludekey":
                    if (ParseExcludeKey(value) is { } excluded)
                        current.Exclude.Add(excluded);
                    break;
            }
        }

        Finish();
        return rules;
    }

    // FileKey=<folder>|<mask>;<mask>[|RECURSE or REMOVESELF]
    private static IEnumerable<RulePath> ParseFileKey(string value)
    {
        var parts = value.Split('|');
        if (parts.Length < 2)
            yield break;

        var masks = parts[1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var recurse = parts.Length > 2 &&
                      (parts[2].Equals("RECURSE", StringComparison.OrdinalIgnoreCase) ||
                       parts[2].Equals("REMOVESELF", StringComparison.OrdinalIgnoreCase));

        foreach (var folder in ExpandProgramFiles(MapVariables(parts[0].TrimEnd('\\'))))
        {
            yield return new RulePath
            {
                Path = folder,
                Masks = masks.Contains("*.*") || masks.Contains("*") ? new List<string>() : masks,
                Recurse = recurse
            };
        }
    }

    // ExcludeKey=FILE|<folder>\|<file>  or  PATH|<folder>\|<mask>. A PATH exclusion keeps the whole
    // folder, which is broader than winapp2 means but errs on the side of keeping files.
    private static string? ParseExcludeKey(string value)
    {
        var parts = value.Split('|');
        if (parts.Length < 2)
            return null;

        var folder = MapVariables(parts[1].TrimEnd('\\'));
        return parts[0].ToUpperInvariant() switch
        {
            "FILE" when parts.Length > 2 && !parts[2].Contains('*') && !parts[2].Contains(';') => folder + "\\" + parts[2],
            "FILE" or "PATH" => folder,
            _ => null // REG exclusions do not apply
        };
    }

    private static string MapVariables(string path)
    {
        foreach (var (name, value) in Variables)
            path = path.Replace(name, value, StringComparison.OrdinalIgnoreCase);
        return path;
    }

    // In winapp2, %ProgramFiles% means both the 64-bit and the 32-bit folder
    private static IEnumerable<string> ExpandProgramFiles(string path)
    {
        yield return path;
        if (path.Contains("%ProgramFiles%", StringComparison.OrdinalIgnoreCase))
            yield return path.Replace("%ProgramFiles%", "%ProgramFiles(x86)%", StringComparison.OrdinalIgnoreCase);
    }
}
