using System.Text.Json;
using YrClean.Core.Models;

namespace YrClean.Core.Services;

public static class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(AppPaths.DataDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static CleanSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new CleanSettings();

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<CleanSettings>(json) ?? new CleanSettings();

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(nameof(CleanSettings.NotifyOnComplete), out _) &&
                settings.Frequency == ScheduleFrequency.Hourly)
            {
                // Frequency=0 meant Daily before Hourly was introduced.
                settings.Frequency = ScheduleFrequency.Daily;
            }

            return settings;
        }
        catch (Exception)
        {
            // Corrupt or unreadable settings file — fall back to defaults rather than crashing
            return new CleanSettings();
        }
    }

    public static void Save(CleanSettings settings)
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
