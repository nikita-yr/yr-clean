using System.Text.Json;
using System.Text.Json.Serialization;
using YrClean.Core.Services;

namespace YrClean.Core.Models;

// One cleanup rule. Built-in rules, user rules, downloaded rules and winapp2 imports all end up
// as this type, and all of them are subject to the same SafeDeleteService checks.
public class CacheSource
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Other";
    public RuleLevel Level { get; set; } = RuleLevel.Safe;

    // The rule is shown only if one of these matches: a path that exists, a registry key
    // ("HKCU\...", "HKLM\...") or an installed program ("app:Name"). Empty means always relevant.
    public List<string> Detect { get; set; } = new();

    public List<RulePath> Paths { get; set; } = new();

    // Process names (without .exe). While any of them runs, the rule is skipped as "busy".
    public List<string> Process { get; set; } = new();

    // Can only make the global minimum age stricter, never looser.
    public int MinAgeDays { get; set; }

    // Folders or files inside the rule's paths that it must leave alone.
    public List<string> Exclude { get; set; } = new();

    public IEnumerable<string> ExpandedExcludes => Exclude.Select(Environment.ExpandEnvironmentVariables);

    // Expands env vars and wildcard segments into the real folders that exist right now,
    // minus anything excluded by the user or by the rule itself.
    public List<ResolvedRoot> Resolve(IEnumerable<string> excludedPaths)
    {
        var excluded = excludedPaths.Concat(ExpandedExcludes).ToList();
        return Paths
            .SelectMany(spec => PathWildcardResolver
                .Resolve(Environment.ExpandEnvironmentVariables(spec.Path))
                .Select(path => new ResolvedRoot(ProtectedLocations.Normalize(path), spec)))
            .Where(root => !ExclusionFilter.IsExcluded(root.Path, excluded))
            .ToList();
    }

    public bool IsBusy() => Process.Any(name =>
    {
        var running = System.Diagnostics.Process.GetProcessesByName(name);
        foreach (var process in running)
            process.Dispose();
        return running.Length > 0;
    });
}

// A folder to clean. In JSON this is either a plain string (everything inside, recursively)
// or an object: { "path": "...", "masks": ["*.log"], "recurse": false }.
[JsonConverter(typeof(RulePathConverter))]
public class RulePath
{
    // May contain %ENV_VAR% placeholders and "*" wildcard folder segments
    public string Path { get; set; } = string.Empty;

    // File name patterns; empty means every file
    public List<string> Masks { get; set; } = new();

    public bool Recurse { get; set; } = true;
}

public record ResolvedRoot(string Path, RulePath Spec);

public class RulePathConverter : JsonConverter<RulePath>
{
    public override RulePath Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var element = document.RootElement;
        if (element.ValueKind == JsonValueKind.String)
            return new RulePath { Path = element.GetString()! };
        if (element.ValueKind != JsonValueKind.Object)
            throw new JsonException("A path must be a string or an object.");

        var path = new RulePath();
        foreach (var property in element.EnumerateObject())
        {
            switch (property.Name.ToLowerInvariant())
            {
                case "path": path.Path = property.Value.GetString() ?? ""; break;
                case "masks": path.Masks = property.Value.EnumerateArray().Select(m => m.GetString() ?? "").ToList(); break;
                case "recurse": path.Recurse = property.Value.GetBoolean(); break;
            }
        }

        return path;
    }

    public override void Write(Utf8JsonWriter writer, RulePath value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("path", value.Path);
        writer.WriteStartArray("masks");
        foreach (var mask in value.Masks)
            writer.WriteStringValue(mask);
        writer.WriteEndArray();
        writer.WriteBoolean("recurse", value.Recurse);
        writer.WriteEndObject();
    }
}
