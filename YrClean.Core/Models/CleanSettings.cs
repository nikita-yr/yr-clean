namespace YrClean.Core.Models;

public enum ScheduleFrequency
{
    Hourly,
    Daily,
    Weekly
}

public class CleanSettings
{
    public int MinAgeDays { get; set; } = 14;

    public List<string> DisabledSourceNames { get; set; } = new();
    public List<string> ExcludedPaths { get; set; } = new();

    public bool ScheduleEnabled { get; set; } = true;
    public ScheduleFrequency Frequency { get; set; } = ScheduleFrequency.Daily;

    // Ignored when Frequency == Hourly
    public string RunAtTime { get; set; } = "03:00";
    public DayOfWeek WeeklyDay { get; set; } = DayOfWeek.Sunday;

    // Default: fully silent. When true, shows a Windows balloon notification after each run.
    public bool NotifyOnComplete { get; set; } = false;

    // Quarantined files are permanently deleted after this many days.
    public int QuarantineDays { get; set; } = 7;

    // Load winapp2.ini from the user rules folder if present. Imported rules are always Review level.
    public bool UseWinapp2 { get; set; } = false;
}