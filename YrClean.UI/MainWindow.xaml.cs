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
    private int _quarantineDays = 7;
    private List<string> _excludedPaths = new();
    private Dictionary<string, int> _minAgeOverrides = new();
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
        _quarantineDays = settings.QuarantineDays;
        _excludedPaths = settings.ExcludedPaths;
        CleanButton.Content = L.T("Main_CleanOlderThan", _minAgeDays);

        var lowDrives = DiskAnalyzer.LowSpaceDrives(settings.LowSpaceThresholdPercent);
        LowSpaceText.Text = L.T("Main_LowSpace", L.LowSpace(lowDrives), settings.LowSpaceThresholdPercent);
        LowSpaceText.Visibility = lowDrives.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var sources = RuleEngine.LoadRelevant(settings).Concat(AutoDiscoveryScanner.Discover());
        var scans = SourceScanner.Scan(sources, settings.ExcludedPaths);
        var nodes = scans.Select(scan => new CacheSourceNode(scan)).ToList();
        var roots = scans.Where(scan => !scan.Busy).SelectMany(scan => scan.Roots).ToList();
        _minAgeOverrides = SourceScanner.MinAgeOverrides(scans);

        _lastScanNodes = nodes.OrderByDescending(node => node.TotalSizeBytes).ToList();
        _lastScanRoots = roots;
        ResultsTree.ItemsSource = _lastScanNodes;

        // Only Safe sources are pre-selected; Review and Expert need a deliberate tick
        SelectAllCheck.IsChecked = false;
        foreach (var node in _lastScanNodes)
            node.IsSelected = node.Level == RuleLevel.Safe;
    }

    // Expert sources are never bulk-selected
    private void SetAllSelected(bool isSelected)
    {
        foreach (var node in _lastScanNodes)
            node.IsSelected = isSelected && node.Level != RuleLevel.Expert;
    }

    private void SelectAllCheck_Checked(object sender, RoutedEventArgs e) => SetAllSelected(true);

    private void SelectAllCheck_Unchecked(object sender, RoutedEventArgs e) => SetAllSelected(false);

    private void CleanButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedFiles = GetSelectedFilePaths();
        if (selectedFiles.Count == 0)
        {
            ShowInfo(L.T("Common_NoFilesSelected"), L.T("Main_Clean"));
            return;
        }

        var classification = SafeDeleteService.Classify(selectedFiles, _lastScanRoots, _minAgeDays, _excludedPaths, _minAgeOverrides);
        if (classification.TotalCount == 0)
        {
            ShowInfo(
                L.T("Main_NoneCleanable", selectedFiles.Count) + "\n\n" +
                BuildClassificationSummary(classification, forConfirm: false),
                L.T("Main_NothingToClean"));
            return;
        }

        if (!ConfirmCleanup(classification))
            return;

        if (!NeedsElevatedHelper(classification))
        {
            // Everything is deleted in this process, so the summary reports exact final numbers.
            var allFiles = classification.DeletableNow.Concat(classification.NeedsAdmin).ToList();
            var result = SafeDeleteService.DeleteFiles(allFiles, _lastScanRoots, _minAgeDays, _excludedPaths, minAgeOverrides: _minAgeOverrides);
            CleanupLog.Write("manual", result);

            ShowInfo(BuildDeleteSummary(result, allFiles.Count), L.T("Main_CleanupComplete"));
            RunScan();
            return;
        }

        var partialResult = SafeDeleteService.DeleteFiles(classification.DeletableNow, _lastScanRoots, _minAgeDays, _excludedPaths, minAgeOverrides: _minAgeOverrides);
        CleanupLog.Write("manual", partialResult);

        var adminCount = classification.NeedsAdmin.Count;
        var helperNote = StartElevatedCleanup(classification.NeedsAdmin)
            ? "\n\n" + L.T("Main_AdminBackground", adminCount)
            : "\n\n" + L.T("Main_AdminCancelled", adminCount);

        ShowInfo(BuildDeleteSummary(partialResult, classification.DeletableNow.Count) + helperNote, L.T("Main_CleanupComplete"));

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
            L.T("Main_MovedOf", result.DeletedCount, attemptedCount),
            L.T("Main_SizeFreedLater", SizeFormatter.Format(result.FreedBytes), _quarantineDays)
        };

        if (result.SkippedCount > 0)
        {
            lines.Add("");
            lines.Add(L.T("Main_LeftInPlace", result.SkippedCount));
            AddSkipReasons(lines, result);
            AddIfAny(lines, result.SkippedAccessDeniedCount, L.T("Skip_AccessDenied"));
            AddIfAny(lines, result.SkippedOtherErrorCount, L.T("Skip_OtherErrors"));
            lines.Add(L.T("Main_DetailsLog"));
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
            L.T("Main_ConfirmText", classification.TotalCount, SizeFormatter.Format(classification.TotalBytes), _quarantineDays) +
            "\n\n" + ExpertWarning() +
            BuildClassificationSummary(classification, forConfirm: true) +
            "\n" + L.T("Common_Proceed"),
            L.T("Main_ConfirmTitle"),
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
            ExcludedPaths = _excludedPaths,
            MinAgeDays = _minAgeDays,
            MinAgeOverrides = _minAgeOverrides
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
            lines.Add("- " + L.T("Main_MovableNow", classification.DeletableNow.Count, SizeFormatter.Format(classification.DeletableNowBytes)));
            lines.Add("- " + L.T("Main_NeedAdmin", classification.NeedsAdmin.Count, SizeFormatter.Format(classification.NeedsAdminBytes)));
        }

        AddSkipReasons(lines, classification);
        return string.Join("\n", lines);
    }

    private void AddSkipReasons(List<string> lines, SkipStatistics stats)
    {
        AddIfAny(lines, stats.SkippedTooRecentCount, L.T("Skip_TooRecent", _minAgeDays));
        AddIfAny(lines, stats.SkippedProtectedCount, L.T("Skip_Protected"));
        AddIfAny(lines, stats.SkippedExcludedCount, L.T("Skip_Excluded"));
        AddIfAny(lines, stats.SkippedReparsePointCount, L.T("Skip_Reparse"));
        AddIfAny(lines, stats.SkippedOutsideRootCount, L.T("Skip_OutsideRoot"));
        AddIfAny(lines, stats.SkippedMissingCount, L.T("Skip_Missing"));
        AddIfAny(lines, stats.SkippedInUseCount, L.T("Skip_InUse"));
    }

    // "reason: count" reads naturally in every language, with no plural forms to get wrong
    private static void AddIfAny(List<string> lines, int count, string description)
    {
        if (count > 0) lines.Add($"- {description}: {count}");
    }

    private string ExpertWarning()
    {
        var expertNames = _lastScanNodes
            .Where(source => source.Level == RuleLevel.Expert &&
                             source.Children.SelectMany(group => group.Children).Any(file => file.IsSelected))
            .Select(source => source.Name)
            .ToList();

        return expertNames.Count == 0
            ? ""
            : L.T("Main_ExpertWarning", string.Join(", ", expertNames)) + "\n\n";
    }

    private void ToolsButton_Click(object sender, RoutedEventArgs e)
    {
        new ToolsWindow { Owner = this }.ShowDialog();
        RunScan();
    }

    private void StartupButton_Click(object sender, RoutedEventArgs e) =>
        new StartupWindow { Owner = this }.ShowDialog();

    private void DiskButton_Click(object sender, RoutedEventArgs e) =>
        new DiskWindow { Owner = this }.ShowDialog();

    private void LeftoversButton_Click(object sender, RoutedEventArgs e) =>
        new LeftoversWindow { Owner = this }.ShowDialog();

    private void QuarantineButton_Click(object sender, RoutedEventArgs e)
    {
        new QuarantineWindow { Owner = this }.ShowDialog();
        RunScan();
    }

    private void ScheduleButton_Click(object sender, RoutedEventArgs e)
    {
        var languageBefore = SettingsService.Load().Language;
        new SettingsWindow { Owner = this }.ShowDialog();

        var language = SettingsService.Load().Language;
        if (language == languageBefore)
        {
            RunScan();
            return;
        }

        // XAML text is resolved when a window is built, so a language change rebuilds the window.
        // Nothing is lost: the scan it shows is re-run from the same saved settings.
        L.Apply(language);
        var replacement = new MainWindow { Left = Left, Top = Top, Width = Width, Height = Height };
        System.Windows.Application.Current.MainWindow = replacement;
        replacement.Show();
        Close();
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

        ShowInfo(L.T("Main_ExclusionAdded", path), L.T("Main_ExclusionsTitle"));
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
