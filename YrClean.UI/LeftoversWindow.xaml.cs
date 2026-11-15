using System.Diagnostics;
using System.Windows;
using YrClean.Core.Services;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;

namespace YrClean.UI;

// What uninstalled programs left behind. Services, tasks and shortcuts are shown for review only;
// folders can be ticked and moved to quarantine by hand.
public partial class LeftoversWindow : Window
{
    private List<LeftoverRow> _rows = new();

    public LeftoversWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += async (_, _) => await Reload();
    }

    private async Task Reload()
    {
        QuarantineButton.IsEnabled = false;
        StatusText.Text = "Scanning...";

        List<Leftover> found;
        try
        {
            found = await Task.Run(() => LeftoverScanner.Scan());
        }
        catch (Exception ex)
        {
            StatusText.Text = "Scan failed: " + ex.Message;
            return;
        }

        _rows = new List<LeftoverRow>();
        AddSection(found, LeftoverKind.Folder, "Folders no installed program seems to own");
        AddSection(found, LeftoverKind.Service, "Services whose program is missing (review only)");
        AddSection(found, LeftoverKind.Task, "Scheduled tasks whose program is missing (review only)");
        AddSection(found, LeftoverKind.Shortcut, "Shortcuts whose target is missing (review only)");
        ResultsList.ItemsSource = _rows;

        QuarantineButton.IsEnabled = found.Any(l => l.Kind == LeftoverKind.Folder);
        StatusText.Text = found.Count == 0
            ? "No leftovers found."
            : "These are candidates, not verdicts - check each one before acting. Nothing is selected or removed automatically.";
    }

    private void AddSection(List<Leftover> found, LeftoverKind kind, string title)
    {
        var items = found.Where(l => l.Kind == kind).OrderByDescending(l => l.Bytes).ToList();
        if (items.Count == 0)
            return;

        _rows.Add(LeftoverRow.Header($"{title}: {items.Count}"));
        _rows.AddRange(items.Select(LeftoverRow.Item));
    }

    private async void Quarantine_Click(object sender, RoutedEventArgs e)
    {
        var folders = _rows.Where(r => r.IsSelected && r.Leftover?.Kind == LeftoverKind.Folder).Select(r => r.Leftover!).ToList();
        if (folders.Count == 0)
        {
            MessageBox.Show(this, "No folders selected.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var settings = SettingsService.Load();
        var confirm = MessageBox.Show(this,
            $"Move the files of {folders.Count} folders ({SizeFormatter.Format(folders.Sum(f => f.Bytes))}) to quarantine?\n\n" +
            string.Join("\n", folders.Take(10).Select(f => f.Path)) +
            $"\n\nThey can be restored for {settings.QuarantineDays} days. Programs, scripts and files used in the last " +
            $"{settings.MinAgeDays} days are left in place.",
            Title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        var moved = 0;
        var left = 0;
        foreach (var folder in folders)
        {
            var result = LeftoverScanner.QuarantineFolder(folder.Path!, settings);
            moved += result.DeletedCount;
            left += result.SkippedCount;
        }

        MessageBox.Show(this, $"Moved to quarantine: {moved} files.\nLeft in place: {left}.", Title,
            MessageBoxButton.OK, MessageBoxImage.Information);
        await Reload();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LeftoverRow { Leftover.Path: { } path } })
            Process.Start("explorer.exe", $"/select,\"{path}\"");
    }
}

public class LeftoverRow
{
    public Leftover? Leftover { get; private init; }
    public string Title { get; private init; } = "";
    public bool IsSelected { get; set; }

    public string Detail => Leftover?.Detail ?? "";
    public string SizeDisplay => Leftover is { Bytes: > 0 } ? SizeFormatter.Format(Leftover.Bytes) : "";
    public FontWeight TitleWeight => Leftover is null ? FontWeights.Bold : FontWeights.Normal;
    public Visibility DetailVisibility => Leftover is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility OpenVisibility => Leftover?.Path is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility CheckVisibility => Leftover?.Kind == LeftoverKind.Folder ? Visibility.Visible : Visibility.Hidden;

    public static LeftoverRow Header(string text) => new() { Title = text };
    public static LeftoverRow Item(Leftover leftover) => new() { Leftover = leftover, Title = leftover.Name };
}
