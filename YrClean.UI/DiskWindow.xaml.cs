using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using YrClean.Core.Services;
using Button = System.Windows.Controls.Button;

namespace YrClean.UI;

// Where the space went: largest folders and files, old files, duplicates. Strictly view-only -
// the only action offered is opening the item's location in Explorer.
public partial class DiskWindow : Window
{
    private const int OldFileDays = 90;

    private CancellationTokenSource? _running;

    public DiskWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);

        FolderBox.Text = Directory.Exists(DiskAnalyzer.DownloadsFolder)
            ? DiskAnalyzer.DownloadsFolder
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var threshold = SettingsService.Load().LowSpaceThresholdPercent;
        DrivesText.Text = string.Join("\n", SystemHealth.GetDriveSpace().Select(d =>
            $"{d.Name}  {SizeFormatter.Format(d.FreeBytes)} free of {SizeFormatter.Format(d.TotalBytes)} ({d.FreePercent:F0}%)" +
            (d.FreePercent < threshold ? $"  -  LOW DISK SPACE (below {threshold}%)" : "")));
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = FolderBox.Text, ShowNewFolderButton = false };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            FolderBox.Text = dialog.SelectedPath;
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e) => await Run((folder, cancellation) =>
    {
        var analysis = DiskAnalyzer.Analyze(folder, cancellation: cancellation);
        return new[] { DiskRow.Header($"Largest folders in {folder}") }
            .Concat(analysis.Folders.Select(DiskRow.Item))
            .Append(DiskRow.Header("Largest files"))
            .Concat(analysis.Files.Select(DiskRow.Item))
            .ToList();
    });

    private async void OldFiles_Click(object sender, RoutedEventArgs e) => await Run((folder, _) =>
    {
        var files = DiskAnalyzer.OldFiles(folder, OldFileDays);
        return new[] { DiskRow.Header($"{files.Count} files not modified for {OldFileDays}+ days, {SizeFormatter.Format(files.Sum(f => f.Bytes))} in total") }
            .Concat(files.Take(500).Select(DiskRow.Item))
            .ToList();
    });

    private async void Duplicates_Click(object sender, RoutedEventArgs e) => await Run((folder, cancellation) =>
    {
        var groups = DiskAnalyzer.FindDuplicates(folder, cancellation: cancellation);
        var wasted = groups.Sum(g => g[0].Bytes * (g.Count - 1));
        var rows = new List<DiskRow> { DiskRow.Header($"{groups.Count} groups of identical files (1 MB and larger), {SizeFormatter.Format(wasted)} in extra copies") };
        foreach (var group in groups.Take(200))
        {
            rows.Add(DiskRow.Header($"{group.Count} copies x {SizeFormatter.Format(group[0].Bytes)}"));
            rows.AddRange(group.Select(DiskRow.Item));
        }

        return rows;
    });

    private async Task Run(Func<string, CancellationToken, List<DiskRow>> work)
    {
        var folder = FolderBox.Text.Trim();
        if (!Directory.Exists(folder))
        {
            StatusText.Text = "That folder does not exist.";
            return;
        }

        _running = new CancellationTokenSource();
        var cancellation = _running.Token;
        ActionsPanel.IsEnabled = false;
        CancelButton.IsEnabled = true;
        StatusText.Text = "Scanning...";

        try
        {
            ResultsList.ItemsSource = await Task.Run(() => work(folder, cancellation), cancellation);
            StatusText.Text = "View only - nothing here is deleted automatically.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Failed: " + ex.Message;
        }
        finally
        {
            _running.Dispose();
            _running = null;
            ActionsPanel.IsEnabled = true;
            CancelButton.IsEnabled = false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _running?.Cancel();

    private void Window_Closing(object? sender, CancelEventArgs e) => _running?.Cancel();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DiskRow { Path: { } path } })
            Process.Start("explorer.exe", $"/select,\"{path}\"");
    }
}

public class DiskRow
{
    public string Title { get; private init; } = "";
    public string Detail { get; private init; } = "";
    public string SizeDisplay { get; private init; } = "";
    public string? Path { get; private init; }

    public FontWeight TitleWeight => Path is null ? FontWeights.Bold : FontWeights.Normal;
    public Visibility OpenVisibility => Path is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DetailVisibility => Detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    public static DiskRow Header(string text) => new() { Title = text };

    public static DiskRow Item(SizedPath item) => new()
    {
        Title = System.IO.Path.GetFileName(item.Path),
        Detail = $"{item.Path}  ·  modified {item.Modified:yyyy-MM-dd}",
        SizeDisplay = SizeFormatter.Format(item.Bytes),
        Path = item.Path
    };
}
