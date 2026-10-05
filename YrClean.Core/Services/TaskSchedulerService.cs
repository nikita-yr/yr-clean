using System.Diagnostics;
using YrClean.Core.Models;

namespace YrClean.Core.Services;

public static class TaskSchedulerService
{
    private const string TaskName = "YrClean AutoClean";

    public static bool Register(CleanSettings settings, string exePath, string vbsPath)
    {
        if (!settings.ScheduleEnabled)
            return Unregister();

        var taskAction = $"wscript.exe \"{vbsPath}\" \"{exePath}\" {CommandLineSwitches.AutoClean}";

        var args = new List<string>
        {
            "/Create", "/F",
            "/TN", TaskName,
            "/TR", taskAction,
            "/SC", settings.Frequency switch
            {
                ScheduleFrequency.Hourly => "HOURLY",
                ScheduleFrequency.Weekly => "WEEKLY",
                _ => "DAILY"
            },
            "/RL", "HIGHEST"
        };

        if (settings.Frequency == ScheduleFrequency.Hourly)
        {
            args.Add("/MO");
            args.Add("1");
        }
        else
        {
            args.Add("/ST");
            args.Add(settings.RunAtTime);

            if (settings.Frequency == ScheduleFrequency.Weekly)
            {
                args.Add("/D");
                args.Add(DayCode(settings.WeeklyDay));
            }
        }

        return RunSchtasks(args);
    }

    public static bool Unregister() =>
        RunSchtasks(new List<string> { "/Delete", "/TN", TaskName, "/F" });

    public static bool Exists()
    {
        var exitCode = RunSchtasksGetExitCode(new List<string> { "/Query", "/TN", TaskName });
        return exitCode == 0;
    }

    private static bool RunSchtasks(List<string> args) => RunSchtasksGetExitCode(args) == 0;

    private static int RunSchtasksGetExitCode(List<string> args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process is null)
                return -1;

            process.WaitForExit();
            return process.ExitCode;
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