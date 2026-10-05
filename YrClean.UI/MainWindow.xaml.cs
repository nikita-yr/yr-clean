using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using YrClean.Core.Models;
using YrClean.Core.Services;

namespace YrClean.UI;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private int _minAgeDays = 14;
    private List<string> _lastScanRoots = new();
    private List<CacheSourceNode> _lastScanNodes = new();

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => UseDarkMode();
        Loaded += (_, _) => RunScan();
    }

    private void UseDarkMode()
    {
        var handle = new WindowInteropHelper(this).Handle;
        int useDarkMode = 1;
        DwmSetWindowAttribute(handle, 20, ref useDarkMode, sizeof(int));
    }

    private void RunScan()
    {
        var settings = SettingsService.Load();
        _minAgeDays = settings.MinAgeDays;
        UpdateCleanButtonLabel();

        var scanner = new FolderScanner();
        var sources = CacheSourceProvider.GetKnownSources()
            .Concat(AutoDiscoveryScanner.Discover())
            .ToList();
        var nodes = new List<CacheSourceNode>();
        var roots = new List<string>();

        foreach (var source in sources)
        {
            var resolvedPaths = source.ResolvePaths()
                .Where(path => !ExclusionFilter.IsExcluded(path, settings.ExcludedPaths))
                .ToList();
            if (resolvedPaths.Count == 0)
                continue;

            var allGroups = new List<CacheGroup>();
            foreach (var path in resolvedPaths)
            {
                allGroups.AddRange(scanner.ScanGrouped(path));
                roots.Add(path);
            }

            if (allGroups.Sum(group => group.TotalSizeBytes) == 0)
                continue;

            nodes.Add(new CacheSourceNode(source, allGroups, resolvedPaths.First()));
        }

        nodes = nodes.OrderByDescending(node => node.TotalSizeBytes).ToList();

        _lastScanNodes = nodes;
        _lastScanRoots = roots;
        ResultsTree.ItemsSource = nodes;

        foreach (var node in nodes) node.IsSelected = true;
        SelectAllCheck.IsChecked = true;
    }

    private void UpdateCleanButtonLabel()
    {
        CleanButton.Content = $"Clean > {_minAgeDays} days";
    }

    private void SelectAllCheck_Checked(object sender, RoutedEventArgs e)
    {
        foreach (var node in _lastScanNodes)
            node.IsSelected = true;
    }

    private void SelectAllCheck_Unchecked(object sender, RoutedEventArgs e)
    {
        foreach (var node in _lastScanNodes)
            node.IsSelected = false;
    }

    private void CleanButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedFiles = _lastScanNodes
            .SelectMany(source => source.Children)
            .SelectMany(group => group.Children)
            .Where(file => file.IsSelected)
            .Select(file => file.FullPath)
            .ToList();

        if (selectedFiles.Count == 0)
        {
            System.Windows.MessageBox.Show("No files selected.", "Clean", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var classification = SafeDeleteService.Classify(selectedFiles, _lastScanRoots, _minAgeDays);
        var totalCount = classification.DeletableNow.Count + classification.NeedsAdmin.Count;

        if (totalCount == 0)
        {
            System.Windows.MessageBox.Show(
                $"None of the {selectedFiles.Count} selected files can be deleted right now.\n\n" +
                BuildClassificationSummary(classification, forConfirm: false),
                "Nothing to clean",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"{totalCount} files will be deleted, freeing " +
            $"{SizeFormatter.Format(classification.DeletableNowBytes + classification.NeedsAdminBytes)}.\n\n" +
            BuildClassificationSummary(classification, forConfirm: true) +
            "\nProceed?",
            "Confirm cleanup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        var result = SafeDeleteService.DeleteFiles(
            classification.DeletableNow,
            _lastScanRoots,
            _minAgeDays);

        if (classification.NeedsAdmin.Count > 0)
        {
            var request = new PendingCleanRequest
            {
                FilePaths = classification.NeedsAdmin,
                AllowedRoots = _lastScanRoots,
                MinAgeDays = _minAgeDays
            };
            var manifestPath = PendingCleanRequestService.Save(request);

            try
            {
                var psi = new ProcessStartInfo(Environment.ProcessPath!)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };
                psi.ArgumentList.Add("--elevated-clean");
                psi.ArgumentList.Add(manifestPath);
                Process.Start(psi);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                PendingCleanRequestService.Delete(manifestPath);
                // User cancelled UAC; the non-admin portion is already deleted.
            }
        }

        System.Windows.MessageBox.Show(
            $"Deleted: {result.DeletedCount} files\nFreed: {SizeFormatter.Format(result.FreedBytes)}" +
            (classification.NeedsAdmin.Count > 0
                ? $"\n\n{classification.NeedsAdmin.Count} more files need administrator rights - finishing in the background (check the Windows notification)."
                : ""),
            "Cleanup complete",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        var rescanTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        rescanTimer.Tick += (_, _) =>
        {
            rescanTimer.Stop();
            RunScan();
        };
        rescanTimer.Start();
    }

    private string BuildClassificationSummary(SafeDeleteService.ClassificationResult classification, bool forConfirm)
    {
        var lines = new List<string>();

        if (forConfirm)
        {
            lines.Add($"- {classification.DeletableNow.Count} deletable now ({SizeFormatter.Format(classification.DeletableNowBytes)})");
            if (classification.NeedsAdmin.Count > 0)
                lines.Add($"- {classification.NeedsAdmin.Count} need administrator rights ({SizeFormatter.Format(classification.NeedsAdminBytes)}) - one UAC prompt");
        }

        if (classification.SkippedTooRecentCount > 0) lines.Add($"- {classification.SkippedTooRecentCount} too recent (accessed within {_minAgeDays} days)");
        if (classification.SkippedProtectedCount > 0) lines.Add($"- {classification.SkippedProtectedCount} protected file type");
        if (classification.SkippedReparsePointCount > 0) lines.Add($"- {classification.SkippedReparsePointCount} reparse point / symlink");
        if (classification.SkippedOutsideRootCount > 0) lines.Add($"- {classification.SkippedOutsideRootCount} outside allowed folders");
        if (classification.SkippedMissingCount > 0) lines.Add($"- {classification.SkippedMissingCount} already gone");
        if (classification.SkippedInUseCount > 0) lines.Add($"- {classification.SkippedInUseCount} in use / locked by another process");

        return string.Join("\n", lines);
    }

    private void RestartElevated()
    {
        try
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            Process.Start(psi);
            System.Windows.Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User cancelled the UAC prompt; keep the current session open.
        }
    }

    private void ScheduleButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow { Owner = this };
        window.ShowDialog();
        RunScan();
    }

    private void AddToExclusions_Click(object sender, RoutedEventArgs e)
    {
        var item = GetClickedItem(sender);
        string? path = item switch
        {
            CacheGroupNode group => group.FullFolderPath,
            CacheSourceNode source => source.FullFolderPath,
            _ => null
        };

        if (path == null)
            return;

        var settings = SettingsService.Load();
        if (!settings.ExcludedPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            settings.ExcludedPaths.Add(path);
            SettingsService.Save(settings);
        }

        System.Windows.MessageBox.Show(
            $"Added to exclusions:\n{path}",
            "Exclusions",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        RunScan();
    }

    private object? GetClickedItem(object sender)
    {
        if (sender is MenuItem menuItem &&
            menuItem.Parent is ContextMenu contextMenu &&
            contextMenu.PlacementTarget is FrameworkElement target)
        {
            return target.DataContext;
        }
        return null;
    }

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        var item = GetClickedItem(sender);

        switch (item)
        {
            case CacheFileNode file:
                Process.Start("explorer.exe", $"/select,\"{file.FullPath}\"");
                break;
            case CacheGroupNode group:
                Process.Start("explorer.exe", $"\"{group.FullFolderPath}\"");
                break;
            case CacheSourceNode source:
                Process.Start("explorer.exe", $"\"{source.FullFolderPath}\"");
                break;
        }
    }

    private void SearchWeb_Click(object sender, RoutedEventArgs e)
    {
        var item = GetClickedItem(sender);
        string? name = item switch
        {
            CacheFileNode file => file.Name,
            CacheGroupNode group => group.Name,
            CacheSourceNode source => source.Name,
            _ => null
        };

        if (name == null) return;

        var query = Uri.EscapeDataString(name);
        Process.Start(new ProcessStartInfo
        {
            FileName = $"https://www.google.com/search?q={query}",
            UseShellExecute = true
        });
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu && menu.PlacementTarget is Grid grid)
        {
            grid.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 0x00, 0xA2, 0xFF));
        }
    }

    private void ContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu && menu.PlacementTarget is Grid grid)
        {
            grid.ClearValue(Grid.BackgroundProperty);
        }
    }
}