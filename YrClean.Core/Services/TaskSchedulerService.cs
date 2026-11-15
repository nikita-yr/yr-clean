using YrClean.Core.Models;

namespace YrClean.Core.Services;

// Each module has its own Task Scheduler entry, so cache cleanup and monthly maintenance
// run on independent schedules.
public static class TaskSchedulerService
{
    private const string CleanTaskName = "YrClean AutoClean";
    private const string MaintenanceTaskName = "YrClean Maintenance";

    // Creates, updates or removes both tasks to match the settings.
    public static bool Register(CleanSettings settings, string exePath, string vbsPath)
    {
        var cleanOk = settings.ScheduleEnabled
            ? Create(CleanTaskName, exePath, vbsPath, CommandLineSwitches.AutoClean, CleanSchedule(settings))
            : Remove(CleanTaskName);

        var maintenanceOk = settings.MaintenanceEnabled
            ? Create(MaintenanceTaskName, exePath, vbsPath, CommandLineSwitches.ComponentCleanup, new[]
              {
                  "/SC", "MONTHLY", "/D", Math.Clamp(settings.MaintenanceDayOfMonth, 1, 28).ToString(), "/ST", settings.RunAtTime
              })
            : Remove(MaintenanceTaskName);

        return cleanOk && maintenanceOk;
    }

    public static bool Exists() => Exists(CleanTaskName);

    private static IEnumerable<string> CleanSchedule(CleanSettings settings) => settings.Frequency switch
    {
        ScheduleFrequency.Hourly => new[] { "/SC", "HOURLY", "/MO", "1" },
        ScheduleFrequency.Weekly => new[] { "/SC", "WEEKLY", "/ST", settings.RunAtTime, "/D", DayCode(settings.WeeklyDay) },
        _ => new[] { "/SC", "DAILY", "/ST", settings.RunAtTime }
    };

    private static bool Create(string taskName, string exePath, string vbsPath, string appSwitch, IEnumerable<string> schedule)
    {
        var action = $"wscript.exe \"{vbsPath}\" \"{exePath}\" {appSwitch}";
        return Schtasks(new[] { "/Create", "/F", "/TN", taskName, "/TR", action, "/RL", "HIGHEST" }.Concat(schedule)) == 0;
    }

    // Removing a task that is not there counts as success
    private static bool Remove(string taskName) =>
        !Exists(taskName) || Schtasks(new[] { "/Delete", "/TN", taskName, "/F" }) == 0;

    private static bool Exists(string taskName) => Schtasks(new[] { "/Query", "/TN", taskName }) == 0;

    private static int Schtasks(IEnumerable<string> args)
    {
        try
        {
            return ProcessRunner.Run(ProcessRunner.System32("schtasks.exe"), args);
        }
        catch (Exception)
        {
            return -1;
        }
    }

    private static string DayCode(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "MON",
        DayOfWeek.Tuesday => "TUE",
        DayOfWeek.Wednesday => "WED",
        DayOfWeek.Thursday => "THU",
        DayOfWeek.Friday => "FRI",
        DayOfWeek.Saturday => "SAT",
        _ => "SUN"
    };
}
