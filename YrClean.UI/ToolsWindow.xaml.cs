using System.ComponentModel;
using System.Windows;
using YrClean.Core.Services;
using MessageBox = System.Windows.MessageBox;

namespace YrClean.UI;

// Windows' own maintenance and repair tools, run one at a time with live output.
public partial class ToolsWindow : Window
{
    private CancellationTokenSource? _running;

    public ToolsWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += async (_, _) => await RefreshSummary();
    }

    private async Task RefreshSummary()
    {
        WindowsOldButton.Visibility = SystemMaintenance.WindowsOldExists ? Visibility.Visible : Visibility.Collapsed;

        var (binBytes, binItems) = SystemMaintenance.QueryRecycleBin();
        RecycleBinButton.Content = $"Empty Recycle Bin ({SizeFormatter.Format(binBytes)})";
        RecycleBinButton.IsEnabled = binItems > 0 && _running is null;

        var lines = SystemHealth.GetDriveSpace().Select(d =>
            $"{d.Name}  {SizeFormatter.Format(d.FreeBytes)} free of {SizeFormatter.Format(d.TotalBytes)} ({d.FreePercent:F0}%)").ToList();
        SummaryText.Text = string.Join("\n", lines) + "\nDisk health: checking...";

        // SMART is read through PowerShell, which takes a second or two
        var disks = await Task.Run(SystemHealth.GetDiskHealth);
        lines.Add(disks.Count == 0
            ? "Disk health: not available on this system"
            : "Disk health: " + string.Join(", ", disks.Select(d => $"{d.Name} - {d.StatusText}")));
        if (disks.Any(d => d.NeedsAttention))
            lines.Add("WARNING: a disk reports problems. Back up your data now.");
        lines.Add($"Startup programs: {StartupManager.GetItems().Count(i => i.Enabled)} enabled");
        SummaryText.Text = string.Join("\n", lines);
    }

    private async void CheckSystem_Click(object sender, RoutedEventArgs e) => await RunOperation(
        "Check system",
        "This runs DISM /RestoreHealth and then sfc /scannow. It can take 15-40 minutes and needs an internet connection.",
        restorePoint: true,
        (log, cancellation) =>
        {
            var results = SystemHealth.CheckSystem(log, cancellation);
            return string.Join("\n\n", results.Select(r =>
                $"{r.Tool}: {(r.Succeeded ? "completed" : $"reported a problem (exit code {r.ExitCode})")}\n{r.Verdict}"));
        });

    private async void ComponentCleanup_Click(object sender, RoutedEventArgs e) => await RunOperation(
        "Clean up component store",
        "DISM will remove superseded Windows components. Updates installed so far can no longer be uninstalled afterwards.",
        restorePoint: true,
        (log, cancellation) =>
        {
            var result = SystemMaintenance.ComponentCleanup(log, cancellation);
            return (result.Succeeded ? "Completed." : $"DISM reported a problem (exit code {result.ExitCode}).") + "\n" + result.Verdict;
        });

    private async void UpdateCache_Click(object sender, RoutedEventArgs e) => await RunOperation(
        "Clear Windows Update cache",
        "The Windows Update service is stopped briefly while old downloaded update files are moved to quarantine.",
        restorePoint: false,
        (log, _) =>
        {
            var result = SystemMaintenance.ClearUpdateCache(SettingsService.Load(), log);
            return $"Moved to quarantine: {result.DeletedCount} files, {SizeFormatter.Format(result.FreedBytes)}.\n" +
                   $"Left in place: {result.SkippedCount} (too recent, protected or in use).";
        });

    private async void RecycleBin_Click(object sender, RoutedEventArgs e) => await RunOperation(
        "Empty Recycle Bin",
        "Everything in the Recycle Bin on all drives is deleted permanently. This cannot be undone.",
        restorePoint: false,
        (_, _) => SystemMaintenance.EmptyRecycleBin() ? "The Recycle Bin is empty." : "Windows could not empty the Recycle Bin.");

    private async void WindowsOld_Click(object sender, RoutedEventArgs e) => await RunOperation(
        "Remove Windows.old",
        $"{SystemMaintenance.WindowsOldPath} holds your previous Windows installation. After removing it you can no longer " +
        "go back to that version, and it cannot be restored from quarantine.",
        restorePoint: true,
        (log, cancellation) => SystemMaintenance.RemoveWindowsOld(log, cancellation).Verdict);

    private async void RestorePoint_Click(object sender, RoutedEventArgs e) => await RunOperation(
        "Create restore point",
        "Creates a System Restore checkpoint. Windows allows one per 24 hours and skips the request otherwise.",
        restorePoint: false,
        (log, _) => SystemMaintenance.CreateRestorePoint("YrClean manual restore point", log)
            ? "Restore point requested."
            : "Windows refused to create a restore point. System Restore may be turned off for the system drive.");

    // Confirms, optionally makes a restore point, then runs the work off the UI thread.
    private async Task RunOperation(
        string title, string explanation, bool restorePoint, Func<Action<string>, CancellationToken, string> work)
    {
        var confirm = MessageBox.Show(this,
            explanation + (restorePoint ? "\n\nA restore point is created first." : "") + "\n\nProceed?",
            title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        _running = new CancellationTokenSource();
        var cancellation = _running.Token;
        SetBusy(true, title + "...");
        OutputBox.Clear();

        string summary;
        try
        {
            if (restorePoint && !await Task.Run(() => SystemMaintenance.CreateRestorePoint("YrClean: " + title, Log)))
            {
                var goOn = MessageBox.Show(this,
                    "A restore point could not be created (System Restore may be turned off).\n\nContinue without one?",
                    title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (goOn != MessageBoxResult.Yes)
                    return;
            }

            summary = await Task.Run(() => work(Log, cancellation), cancellation);
        }
        catch (OperationCanceledException)
        {
            summary = "Cancelled.";
        }
        catch (Exception ex)
        {
            summary = "Failed: " + ex.Message;
        }
        finally
        {
            _running.Dispose();
            _running = null;
            SetBusy(false, "Ready.");
        }

        Log("");
        Log(summary);
        MessageBox.Show(this, summary, title, MessageBoxButton.OK, MessageBoxImage.Information);
        await RefreshSummary();
    }

    // Called from worker threads; progress lines move the bar instead of flooding the log
    private void Log(string line) => Dispatcher.BeginInvoke(() =>
    {
        if (ProcessRunner.TryParsePercent(line) is { } percent)
        {
            Progress.IsIndeterminate = false;
            Progress.Value = percent;
            return;
        }

        OutputBox.AppendText(line + Environment.NewLine);
        OutputBox.ScrollToEnd();
    });

    private void SetBusy(bool busy, string status)
    {
        ActionsPanel.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
        StatusText.Text = status;
        Progress.IsIndeterminate = busy;
        Progress.Value = 0;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _running?.Cancel();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_running is null)
            return;

        e.Cancel = true;
        MessageBox.Show(this, "An operation is still running. Cancel it first.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
