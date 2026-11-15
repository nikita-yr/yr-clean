using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using YrClean.Core.Models;

namespace YrClean.Core.Services;

// Loads cleanup rules from JSON (built-in, downloaded, user-written) and winapp2.ini.
// Every rule is validated here on load, and every file it selects is validated again by
// SafeDeleteService on removal - a rule can never widen what the app is allowed to touch.
public static class RuleEngine
{
    public const int SchemaVersion = 1;
    public const string RemoteRulesFileName = "remote.json";
    public const string Winapp2FileName = "winapp2.ini";

    public static readonly string UserRulesDirectory = Path.Combine(AppPaths.DataDirectory, "rules");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Regex IdPattern = new(@"^[a-z0-9][a-z0-9._-]*$", RegexOptions.Compiled);

    private sealed class RuleDefinition : CacheSource
    {
        // One rule stamped out per variant: "{root}" in paths and "{name}" in the name are replaced.
        public List<RuleVariant>? Variants { get; set; }
    }

    private sealed class RuleVariant
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Root { get; set; } = "";
        public List<string> Process { get; set; } = new();
    }

    // All rules that apply to this machine. Order decides who wins a duplicate id:
    // built-in (or its downloaded replacement), then user rule files, then winapp2.ini.
    public static List<CacheSource> LoadRelevant(CleanSettings settings)
    {
        var errors = new List<string>();
        var rules = new List<CacheSource>();

        string? downloaded = null;
        try
        {
            var remotePath = Path.Combine(UserRulesDirectory, RemoteRulesFileName);
            if (File.Exists(remotePath))
                downloaded = File.ReadAllText(remotePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add(ex.Message);
        }

        rules.AddRange(Parse(ChooseRuleBase(BuiltInJson(), downloaded), out _));

        if (Directory.Exists(UserRulesDirectory))
        {
            foreach (var file in Directory.GetFiles(UserRulesDirectory, "*.json").Order())
            {
                if (!Path.GetFileName(file).Equals(RemoteRulesFileName, StringComparison.OrdinalIgnoreCase))
                    rules.AddRange(TryReadFile(file, errors));
            }

            var winapp2 = Path.Combine(UserRulesDirectory, Winapp2FileName);
            if (settings.UseWinapp2 && File.Exists(winapp2))
            {
                try
                {
                    rules.AddRange(Winapp2Parser.Parse(File.ReadAllText(winapp2)).Where(r => Validate(r) is null));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{winapp2}: {ex.Message}");
                }
            }
        }

        foreach (var error in errors)
            CleanupLog.WriteLine("rules: " + error);

        return rules
            .DistinctBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .Where(IsRelevant)
            .ToList();
    }

    public static List<CacheSource> LoadBuiltIn() => Parse(BuiltInJson(), out _);

    private static string BuiltInJson()
    {
        using var stream = typeof(RuleEngine).Assembly.GetManifestResourceStream("YrClean.Core.Rules.builtin.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // A downloaded rule base replaces the built-in one only if it is usable and at least as new;
    // otherwise an old download would shadow the rules shipped with a newer app version.
    public static string ChooseRuleBase(string builtInJson, string? downloadedJson) =>
        downloadedJson is not null &&
        ReadVersion(downloadedJson) >= ReadVersion(builtInJson) &&
        Parse(downloadedJson, out _).Count > 0
            ? downloadedJson
            : builtInJson;

    // The optional top-level "version" number of a rule file; 0 when absent or unreadable.
    public static int ReadVersion(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("version", out var version) &&
                   version.TryGetInt32(out var number)
                ? number
                : 0;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return 0;
        }
    }

    // Parses a rule file. Invalid rules are dropped and reported in errors; a file with the
    // wrong shape or schema version yields no rules at all.
    public static List<CacheSource> Parse(string json, out List<string> errors)
    {
        errors = new List<string>();
        var rules = new List<CacheSource>();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException ex)
        {
            errors.Add("Not valid JSON: " + ex.Message);
            return rules;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.Number ||
                !schema.TryGetInt32(out var version) || version != SchemaVersion ||
                !root.TryGetProperty("rules", out var ruleArray) || ruleArray.ValueKind != JsonValueKind.Array)
            {
                errors.Add($"Expected {{ \"schema\": {SchemaVersion}, \"rules\": [...] }}.");
                return rules;
            }

            foreach (var element in ruleArray.EnumerateArray())
            {
                try
                {
                    var definition = element.Deserialize<RuleDefinition>(JsonOptions)
                                     ?? throw new JsonException("Rule is null.");
                    foreach (var rule in Expand(definition))
                    {
                        if (Validate(rule) is { } problem)
                            errors.Add($"Rule '{rule.Id}': {problem}");
                        else
                            rules.Add(rule);
                    }
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException)
                {
                    errors.Add("Malformed rule: " + ex.Message);
                }
            }
        }

        return rules;
    }

    // Why a rule is unacceptable, or null when it is fine.
    public static string? Validate(CacheSource rule)
    {
        if (!IdPattern.IsMatch(rule.Id))
            return "id must be lowercase letters, digits, '.', '_' or '-'";
        if (string.IsNullOrWhiteSpace(rule.Name))
            return "name is required";
        if (!Enum.IsDefined(rule.Level))
            return "level must be safe, review or expert";
        if (rule.MinAgeDays < 0)
            return "minAgeDays cannot be negative";
        if (rule.Paths.Count == 0)
            return "at least one path is required";

        foreach (var spec in rule.Paths)
        {
            var expanded = Environment.ExpandEnvironmentVariables(spec.Path);
            var segments = expanded.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (expanded.Contains('%'))
                return $"unknown variable in path: {spec.Path}";
            if (expanded.Length < 3 || expanded[1] != ':' || !Path.IsPathFullyQualified(expanded))
                return $"path must be an absolute local path: {spec.Path}";
            if (segments.Contains("..") || segments.Contains("."))
                return $"path must not contain relative segments: {spec.Path}";

            // The fixed part before the first wildcard decides where the rule can reach
            var fixedPart = string.Join(Path.DirectorySeparatorChar, segments.TakeWhile(s => !s.Contains('*') && !s.Contains('?')));
            if (fixedPart.Length <= 2)
                fixedPart += Path.DirectorySeparatorChar;
            if (ProtectedLocations.IsForbiddenRoot(fixedPart))
                return $"path points at a protected location: {spec.Path}";

            if (spec.Masks.Any(m => m.Length == 0 || m.IndexOfAny(new[] { '\\', '/', ':' }) >= 0))
                return $"file masks must be plain file name patterns: {spec.Path}";
        }

        return null;
    }

    public static bool IsRelevant(CacheSource rule) => rule.Detect.Count == 0 || rule.Detect.Any(DetectMatches);

    private static bool DetectMatches(string detect)
    {
        try
        {
            if (detect.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
                return InstalledPrograms.IsInstalled(detect[4..].Trim());

            if (detect.StartsWith("HK", StringComparison.OrdinalIgnoreCase))
                return RegistryKeyExists(detect);

            var expanded = Environment.ExpandEnvironmentVariables(detect);
            if (expanded.Contains('*'))
            {
                // The pattern may name folders, or files in its last segment
                var folder = Path.GetDirectoryName(expanded) ?? "";
                var pattern = Path.GetFileName(expanded);
                return PathWildcardResolver.Resolve(expanded).Count > 0 ||
                       PathWildcardResolver.Resolve(folder).Any(f => Directory.EnumerateFiles(f, pattern).Any());
            }

            return Directory.Exists(expanded) || File.Exists(expanded);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Read-only existence check; YrClean never writes or cleans registry keys for rules.
    private static bool RegistryKeyExists(string path)
    {
        var separator = path.IndexOf('\\');
        if (separator < 0)
            return false;

        var hive = path[..separator].ToUpperInvariant() switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
            "HKU" or "HKEY_USERS" => Registry.Users,
            _ => null
        };

        using var key = hive?.OpenSubKey(path[(separator + 1)..]);
        return key is not null;
    }

    private static IEnumerable<CacheSource> Expand(RuleDefinition definition)
    {
        if (definition.Variants is not { Count: > 0 } variants)
        {
            yield return definition;
            yield break;
        }

        foreach (var variant in variants)
        {
            yield return new CacheSource
            {
                Id = $"{definition.Id}-{variant.Id}",
                Name = definition.Name.Replace("{name}", variant.Name),
                Category = definition.Category,
                Level = definition.Level,
                MinAgeDays = definition.MinAgeDays,
                Detect = definition.Detect.Select(d => d.Replace("{root}", variant.Root)).ToList(),
                Exclude = definition.Exclude.Select(e => e.Replace("{root}", variant.Root)).ToList(),
                Process = definition.Process.Concat(variant.Process).ToList(),
                Paths = definition.Paths.Select(p => new RulePath
                {
                    Path = p.Path.Replace("{root}", variant.Root),
                    Masks = p.Masks,
                    Recurse = p.Recurse
                }).ToList()
            };
        }
    }

    private static List<CacheSource> TryReadFile(string path, List<string> errors)
    {
        try
        {
            var rules = Parse(File.ReadAllText(path), out var fileErrors);
            errors.AddRange(fileErrors.Select(e => $"{path}: {e}"));
            return rules;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add($"{path}: {ex.Message}");
            return new List<CacheSource>();
        }
    }
}
