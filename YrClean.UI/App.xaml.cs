using System.Windows;
using YrClean.Core.Services;

namespace YrClean.UI;

public partial class App : System.Windows.Application
{
    private const string NotificationTitle = "YrClean";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains(CommandLineSwitches.ElevatedClean) && e.Args.Length >= 2)
        {
            RunHeadless(() => RunElevatedClean(manifestPath: e.Args[1]));
            return;
        }

        if (e.Args.Contains(CommandLineSwitches.AutoClean))
        {
            RunHeadless(RunAutoClean);
            return;
        }

        if (e.Args.Contains(CommandLineSwitches.ComponentCleanup))
        {
            RunHeadless(() => RunReport("component-cleanup", log =>
                Reports.Verdicts(new[] { SystemMaintenance.ComponentCleanup(log) })));
            return;
        }

        if (e.Args.Contains(CommandLineSwitches.Health))
        {
            RunHeadless(() => RunReport("health", log => Reports.Verdicts(SystemHealth.CheckSystem(log))));
            return;
        }

        if (e.Args.Contains(CommandLineSwitches.StartupReport))
        {
            RunHeadless(() => RunReport("startup-report", _ => Reports.Startup()));
            return;
        }

        if (e.Args.Contains(CommandLineSwitches.DiskReport))
        {
            RunHeadless(() => RunReport("disk-report", _ => Reports.Disk(SettingsService.Load())));
            return;
        }

        // Quarantined files past their retention period are deleted for good here
        QuarantineService.Default.Purge(SettingsService.Load().QuarantineDays);
        new MainWindow().Show();
    }

    // Runs a windowless job and always exits afterwards. An unhandled exception here would
    // leave a crash dialog (or a stuck process) running as administrator, out of the user's reach.
    private void RunHeadless(Action job)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            job();
        }
        catch (Exception)
        {
            // Headless runs have no UI to report failures to; CleanupLog has the details.
        }
        finally
        {
            Shutdown();
        }
    }

    private static void RunElevatedClean(string manifestPath)
    {
        try
        {
            var request = PendingCleanRequestService.Load(manifestPath);
            var result = SafeDeleteService.DeleteFiles(
                request.FilePaths,
                request.AllowedRoots,
                request.MinAgeDays,
                request.ExcludedPaths,
                minAgeOverrides: request.MinAgeOverrides);
            CleanupLog.Write("elevated", result);

            // Report against the number the main window announced, so the two always reconcile
            var message = $"Moved {result.DeletedCount} of {request.FilePaths.Count} administrator-protected files to quarantine, " +
                          $"{SizeFormatter.Format(result.FreedBytes)}.";
            if (result.SkippedCount > 0)
                message += $" {result.SkippedCount} were left in place (details in autoclean.log).";

            TryNotify(message);
        }
        finally
        {
            PendingCleanRequestService.Delete(manifestPath);
        }
    }

    private static void RunAutoClean()
    {
        var settings = SettingsService.Load();
        var result = AutoCleanRunner.Run(settings);

        // Low disk space is worth interrupting for even when routine notifications are off
        var lowDrives = DiskAnalyzer.LowSpaceDrives(settings.LowSpaceThresholdPercent);
        if (lowDrives.Count > 0)
            TryNotify("Low disk space: " + DiskAnalyzer.LowSpaceMessage(lowDrives));

        if (settings.NotifyOnComplete)
            TryNotify($"Moved {result.DeletedCount} files to quarantine, {SizeFormatter.Format(result.FreedBytes)}.");
    }

    // Runs a command-line report: everything is echoed to the calling console when there is one
    // and always saved under %AppData%\YrClean\reports, since a GUI app has no console of its own.
    private static void RunReport(string name, Func<Action<string>, List<string>> produce)
    {
        const int attachParentProcess = -1;
        if (AttachConsole(attachParentProcess))
            Console.SetOut(new System.IO.StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });

        var lines = new List<string> { $"YrClean {name}  {DateTime.Now:yyyy-MM-dd HH:mm}", "" };
        void Log(string line)
        {
            // Progress percentages are console noise in a saved report
            if (ProcessRunner.TryParsePercent(line) is null)
                lines.Add(line);
            Console.WriteLine(line);
        }

        var summary = produce(Log);
        lines.Add("");
        lines.AddRange(summary);
        summary.ForEach(Console.WriteLine);

        var path = Reports.Save(name, lines);
        Console.WriteLine($"Report saved to {path}");
        CleanupLog.WriteLine($"run={name}  report={path}");
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    private static void TryNotify(string message)
    {
        try
        {
            NotificationService.ShowBalloonBlocking(NotificationTitle, message);
        }
        catch (Exception)
        {
            // Notification failure must not crash an unattended cleanup.
        }
    }
}
