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
        RecycleBinButton.Content = L.T("Tools_RecycleBinSize", SizeFormatter.Format(binBytes));
        RecycleBinButton.IsEnabled = binItems > 0 && _running is null;

        var threshold = SettingsService.Load().LowSpaceThresholdPercent;
        var lines = SystemHealth.GetDriveSpace().Select(d =>
            L.DriveLine(d) + (d.FreePercent < threshold ? "  -  " + L.T("Drive_Low", threshold) : "")).ToList();
        SummaryText.Text = string.Join("\n", lines) + "\n" + L.T("Tools_HealthChecking");

        // SMART is read through PowerShell, which takes a second or two
        var disks = await Task.Run(SystemHealth.GetDiskHealth);
        lines.Add(disks.Count == 0
            ? L.T("Tools_HealthUnavailable")
            : L.T("Tools_Health", string.Join(", ", disks.Select(d => $"{d.Name} - {HealthText(d)}"))));
        if (disks.Any(d => d.NeedsAttention))
            lines.Add(L.T("Tools_HealthWarning"));
        lines.Add(L.T("Tools_StartupCount", StartupManager.GetItems().Count(i => i.Enabled)));
        SummaryText.Text = string.Join("\n", lines);
    }

    private static string HealthText(DiskHealth disk) => L.T(disk.Status switch
    {
        0 => "Health_Healthy",
        1 => "Health_Warning",
        2 => "Health_Unhealthy",
        _ => "Health_Unknown"
    });

    // The verdict is the tool's own closing message, already in the Windows display language
    private static string ToolLine(ToolResult result) =>
        (result.Succeeded ? L.T("Tools_ToolCompleted", result.Tool) : L.T("Tools_ToolProblem", result.Tool, result.ExitCode)) +
        "\n" + result.Verdict;

    private async void CheckSystem_Click(object sender, RoutedEventArgs e) => await RunOperation(
        L.T("Tools_Check"), L.T("Tools_CheckExplain"), restorePoint: true,
        (log, cancellation) => string.Join("\n\n", SystemHealth.CheckSystem(log, cancellation).Select(ToolLine)));

    private async void ComponentCleanup_Click(object sender, RoutedEventArgs e) => await RunOperation(
        L.T("Tools_Component"), L.T("Tools_ComponentExplain"), restorePoint: true,
        (log, cancellation) => ToolLine(SystemMaintenance.ComponentCleanup(log, cancellation)));

    private async void UpdateCache_Click(object sender, RoutedEventArgs e) => await RunOperation(
        L.T("Tools_UpdateCache"), L.T("Tools_UpdateCacheExplain"), restorePoint: false,
        (log, _) =>
        {
            var result = SystemMaintenance.ClearUpdateCache(SettingsService.Load(), log);
            return L.T("Tools_UpdateCacheResult", result.DeletedCount, SizeFormatter.Format(result.FreedBytes), result.SkippedCount);
        });

    private async void RecycleBin_Click(object sender, RoutedEventArgs e) => await RunOperation(
        L.T("Tools_RecycleBin"), L.T("Tools_RecycleBinExplain"), restorePoint: false,
        (_, _) => SystemMaintenance.EmptyRecycleBin() ? L.T("Tools_RecycleBinDone") : L.T("Tools_RecycleBinFailed"));

    private async void WindowsOld_Click(object sender, RoutedEventArgs e) => await RunOperation(
        L.T("Tools_WindowsOld"), L.T("Tools_WindowsOldExplain", SystemMaintenance.WindowsOldPath), restorePoint: true,
        (log, cancellation) =>
        {
            SystemMaintenance.RemoveWindowsOld(log, cancellation);
            return SystemMaintenance.WindowsOldExists ? L.T("Tools_WindowsOldStill") : L.T("Tools_WindowsOldRemoved");
        });

    private async void RestorePoint_Click(object sender, RoutedEventArgs e) => await RunOperation(
        L.T("Tools_RestorePoint"), L.T("Tools_RestorePointExplain"), restorePoint: false,
        (log, _) => SystemMaintenance.CreateRestorePoint("YrClean manual restore point", log)
            ? L.T("Tools_RestorePointDone")
            : L.T("Tools_RestorePointFailed"));

    // Confirms, optionally makes a restore point, then runs the work off the UI thread.
    private async Task RunOperation(
        string title, string explanation, bool restorePoint, Func<Action<string>, CancellationToken, string> work)
    {
        var confirm = MessageBox.Show(this,
            explanation + (restorePoint ? "\n\n" + L.T("Tools_RestorePointFirst") : "") + "\n\n" + L.T("Common_Proceed"),
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
            // The restore point description is stored by Windows, so it stays English
            if (restorePoint && !await Task.Run(() => SystemMaintenance.CreateRestorePoint("YrClean", Log)))
            {
                var goOn = MessageBox.Show(this, L.T("Tools_RestorePointContinue"), title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (goOn != MessageBoxResult.Yes)
                    return;
            }

            summary = await Task.Run(() => work(Log, cancellation), cancellation);
        }
        catch (OperationCanceledException)
        {
            summary = L.T("Common_Cancelled");
        }
        catch (Exception ex)
        {
            summary = L.T("Common_Failed", ex.Message);
        }
        finally
        {
            _running.Dispose();
            _running = null;
            SetBusy(false, L.T("Tools_Ready"));
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
        MessageBox.Show(this, L.T("Tools_StillRunning"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
