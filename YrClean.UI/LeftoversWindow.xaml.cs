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
        StatusText.Text = L.T("Common_Scanning");

        List<Leftover> found;
        try
        {
            found = await Task.Run(() => LeftoverScanner.Scan());
        }
        catch (Exception ex)
        {
            StatusText.Text = L.T("Common_Failed", ex.Message);
            return;
        }

        _rows = new List<LeftoverRow>();
        AddSection(found, LeftoverKind.Folder, L.T("Left_Folders"));
        AddSection(found, LeftoverKind.Service, L.T("Left_Services"));
        AddSection(found, LeftoverKind.Task, L.T("Left_Tasks"));
        AddSection(found, LeftoverKind.Shortcut, L.T("Left_Shortcuts"));
        ResultsList.ItemsSource = _rows;

        QuarantineButton.IsEnabled = found.Any(l => l.Kind == LeftoverKind.Folder);
        StatusText.Text = found.Count == 0 ? L.T("Left_None") : L.T("Left_Hint");
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
            MessageBox.Show(this, L.T("Left_NoneSelected"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var settings = SettingsService.Load();
        var confirm = MessageBox.Show(this,
            L.T("Left_Confirm", folders.Count, SizeFormatter.Format(folders.Sum(f => f.Bytes))) + "\n\n" +
            string.Join("\n", folders.Take(10).Select(f => f.Path)) +
            "\n\n" + L.T("Left_ConfirmNote", settings.QuarantineDays, settings.MinAgeDays),
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

        MessageBox.Show(this, L.T("Left_Result", moved, left), Title,
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

    public string Detail => Leftover is null
        ? ""
        : Leftover.Location + "  ·  " + (Leftover.MissingTarget is null
            ? L.T("Left_FolderDetail", LeftoverScanner.InactiveDays)
            : L.T("Left_MissingDetail", Leftover.MissingTarget));
    public string SizeDisplay => Leftover is { Bytes: > 0 } ? SizeFormatter.Format(Leftover.Bytes) : "";
    public FontWeight TitleWeight => Leftover is null ? FontWeights.Bold : FontWeights.Normal;
    public Visibility DetailVisibility => Leftover is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility OpenVisibility => Leftover?.Path is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility CheckVisibility => Leftover?.Kind == LeftoverKind.Folder ? Visibility.Visible : Visibility.Hidden;

    public static LeftoverRow Header(string text) => new() { Title = text };
    public static LeftoverRow Item(Leftover leftover) => new() { Leftover = leftover, Title = leftover.Name };
}
