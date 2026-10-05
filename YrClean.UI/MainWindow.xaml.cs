using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using YrClean.Core.Models;
using YrClean.Core.Services;
using MessageBox = System.Windows.MessageBox;

namespace YrClean.UI;

public partial class MainWindow : Window
{
    private static readonly TimeSpan RescanDelay = TimeSpan.FromSeconds(3);

    private static readonly SolidColorBrush ContextHighlightBrush = CreateFrozenBrush(0x33, 0x00, 0xA2, 0xFF);

    private int _minAgeDays = 14;
    private List<string> _lastScanRoots = new();
    private List<CacheSourceNode> _lastScanNodes = new();

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += (_, _) => RunScan();
    }

    private void RunScan()
    {
        var settings = SettingsService.Load();
        _minAgeDays = settings.MinAgeDays;
        CleanButton.Content = $"Clean > {_minAgeDays} days";

        var scanner = new FolderScanner();
        var sources = CacheSourceProvider.GetKnownSources().Concat(AutoDiscoveryScanner.Discover());
        var nodes = new List<CacheSourceNode>();
        var roots = new List<string>();

        foreach (var source in sources)
        {
            var resolvedPaths = source.ResolvePaths(settings.ExcludedPaths);
            if (resolvedPaths.Count == 0)
                continue;

            roots.AddRange(resolvedPaths);
            var allGroups = resolvedPaths.SelectMany(scanner.ScanGrouped).ToList();

            if (allGroups.Sum(group => group.TotalSizeBytes) == 0)
                continue;

            nodes.Add(new CacheSourceNode(source, allGroups, resolvedPaths[0]));
        }

        _lastScanNodes = nodes.OrderByDescending(node => node.TotalSizeBytes).ToList();
        _lastScanRoots = roots;
        ResultsTree.ItemsSource = _lastScanNodes;

        SetAllSelected(true);
        SelectAllCheck.IsChecked = true;
    }

    private void SetAllSelected(bool isSelected)
    {
        foreach (var node in _lastScanNodes)
            node.IsSelected = isSelected;
    }

    private void SelectAllCheck_Checked(object sender, RoutedEventArgs e) => SetAllSelected(true);

    private void SelectAllCheck_Unchecked(object sender, RoutedEventArgs e) => SetAllSelected(false);

    private void CleanButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedFiles = GetSelectedFilePaths();
        if (selectedFiles.Count == 0)
        {
            ShowInfo("No files selected.", "Clean");
            return;
        }

        var classification = SafeDeleteService.Classify(selectedFiles, _lastScanRoots, _minAgeDays);
        if (classification.TotalCount == 0)
        {
            ShowInfo(
                $"None of the {selectedFiles.Count} selected files can be deleted right now.\n\n" +
                BuildClassificationSummary(classification, forConfirm: false),
                "Nothing to clean");
            return;
        }

        if (!ConfirmCleanup(classification))
            return;

        if (!NeedsElevatedHelper(classification))
        {
            // Everything is deleted in this process, so the summary reports exact final numbers.
            var allFiles = classification.DeletableNow.Concat(classification.NeedsAdmin).ToList();
            var result = SafeDeleteService.DeleteFiles(allFiles, _lastScanRoots, _minAgeDays);
            CleanupLog.Write("manual", result);

            ShowInfo(BuildDeleteSummary(result, allFiles.Count), "Cleanup complete");
            RunScan();
            return;
        }

        var partialResult = SafeDeleteService.DeleteFiles(classification.DeletableNow, _lastScanRoots, _minAgeDays);
        CleanupLog.Write("manual", partialResult);

        var adminCount = classification.NeedsAdmin.Count;
        var helperNote = StartElevatedCleanup(classification.NeedsAdmin)
            ? $"\n\n{adminCount} more files need administrator rights - finishing in the background. " +
              "The Windows notification will report how many of them were deleted."
            : $"\n\n{adminCount} files that need administrator rights were not deleted (UAC prompt cancelled).";

        ShowInfo(BuildDeleteSummary(partialResult, classification.DeletableNow.Count) + helperNote, "Cleanup complete");

        // Give the elevated helper a moment before refreshing the tree
        ScheduleRescan();
    }

    // Only a non-admin session has to hand files off to a separate elevated process
    private static bool NeedsElevatedHelper(ClassificationResult classification) =>
        classification.NeedsAdmin.Count > 0 && !ElevatedProcess.IsCurrentProcessElevated;

    private string BuildDeleteSummary(DeleteResult result, int attemptedCount)
    {
        var lines = new List<string>
        {
            $"Deleted: {result.DeletedCount} of {attemptedCount} files",
            $"Freed: {SizeFormatter.Format(result.FreedBytes)}"
        };

        if (result.SkippedCount > 0)
        {
            lines.Add("");
            lines.Add($"Not deleted: {result.SkippedCount}");
            AddSkipReasons(lines, result);
            AddIfAny(lines, result.SkippedAccessDeniedCount, "access denied");
            AddIfAny(lines, result.SkippedOtherErrorCount, "other errors");
            lines.Add("Details: autoclean.log");
        }

        return string.Join("\n", lines);
    }

    private List<string> GetSelectedFilePaths() =>
        _lastScanNodes
            .SelectMany(source => source.Children)
            .SelectMany(group => group.Children)
            .Where(file => file.IsSelected)
            .Select(file => file.FullPath)
            .ToList();

    private bool ConfirmCleanup(ClassificationResult classification)
    {
        var confirm = MessageBox.Show(
            $"{classification.TotalCount} files will be deleted, freeing " +
            $"{SizeFormatter.Format(classification.TotalBytes)}.\n\n" +
            BuildClassificationSummary(classification, forConfirm: true) +
            "\nProceed?",
            "Confirm cleanup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return confirm == MessageBoxResult.Yes;
    }

    // Hands admin-only files to an elevated copy of the app via a temp manifest.
    // Returns false if the user cancelled the UAC prompt.
    private bool StartElevatedCleanup(List<string> filePaths)
    {
        var request = new PendingCleanRequest
        {
            FilePaths = filePaths,
            AllowedRoots = _lastScanRoots,
            MinAgeDays = _minAgeDays
        };
        var manifestPath = PendingCleanRequestService.Save(request);

        if (ElevatedProcess.TryStartSelf(CommandLineSwitches.ElevatedClean, manifestPath))
            return true;

        // User cancelled UAC; the non-admin portion is already deleted.
        PendingCleanRequestService.Delete(manifestPath);
        return false;
    }

    private void ScheduleRescan()
    {
        var rescanTimer = new DispatcherTimer { Interval = RescanDelay };
        rescanTimer.Tick += (_, _) =>
        {
            rescanTimer.Stop();
            RunScan();
        };
        rescanTimer.Start();
    }

    private string BuildClassificationSummary(ClassificationResult classification, bool forConfirm)
    {
        var lines = new List<string>();

        if (forConfirm && NeedsElevatedHelper(classification))
        {
            lines.Add($"- {classification.DeletableNow.Count} deletable now ({SizeFormatter.Format(classification.DeletableNowBytes)})");
            lines.Add($"- {classification.NeedsAdmin.Count} need administrator rights ({SizeFormatter.Format(classification.NeedsAdminBytes)}) - one UAC prompt");
        }

        AddSkipReasons(lines, classification);
        return string.Join("\n", lines);
    }

    private void AddSkipReasons(List<string> lines, SkipStatistics stats)
    {
        AddIfAny(lines, stats.SkippedTooRecentCount, $"too recent (accessed within {_minAgeDays} days)");
        AddIfAny(lines, stats.SkippedProtectedCount, "protected file type");
        AddIfAny(lines, stats.SkippedReparsePointCount, "reparse point / symlink");
        AddIfAny(lines, stats.SkippedOutsideRootCount, "outside allowed folders");
        AddIfAny(lines, stats.SkippedMissingCount, "already gone");
        AddIfAny(lines, stats.SkippedInUseCount, "in use / locked by another process");
    }

    private static void AddIfAny(List<string> lines, int count, string description)
    {
        if (count > 0) lines.Add($"- {count} {description}");
    }

    private void ScheduleButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow { Owner = this };
        window.ShowDialog();
        RunScan();
    }

    private void AddToExclusions_Click(object sender, RoutedEventArgs e)
    {
        if (GetClickedItem(sender) is not FolderNodeBase folder)
            return;

        var path = folder.FullFolderPath;
        var settings = SettingsService.Load();
        if (!settings.ExcludedPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            settings.ExcludedPaths.Add(path);
            SettingsService.Save(settings);
        }

        ShowInfo($"Added to exclusions:\n{path}", "Exclusions");
        RunScan();
    }

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        switch (GetClickedItem(sender))
        {
            case CacheFileNode file:
                Process.Start("explorer.exe", $"/select,\"{file.FullPath}\"");
                break;
            case FolderNodeBase folder:
                Process.Start("explorer.exe", $"\"{folder.FullFolderPath}\"");
                break;
        }
    }

    private void SearchWeb_Click(object sender, RoutedEventArgs e)
    {
        if (GetClickedItem(sender) is not SelectableNodeBase node)
            return;

        var query = Uri.EscapeDataString(node.Name);
        Process.Start(new ProcessStartInfo
        {
            FileName = $"https://www.google.com/search?q={query}",
            UseShellExecute = true
        });
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu { PlacementTarget: Grid grid })
            grid.Background = ContextHighlightBrush;
    }

    private void ContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu { PlacementTarget: Grid grid })
            grid.ClearValue(Grid.BackgroundProperty);
    }

    private static object? GetClickedItem(object sender) =>
        sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement target } }
            ? target.DataContext
            : null;

    private static void ShowInfo(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    private static SolidColorBrush CreateFrozenBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
