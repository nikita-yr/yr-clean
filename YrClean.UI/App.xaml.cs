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

        if (settings.NotifyOnComplete)
            TryNotify($"Moved {result.DeletedCount} files to quarantine, {SizeFormatter.Format(result.FreedBytes)}.");
    }

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
