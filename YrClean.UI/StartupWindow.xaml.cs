using System.Windows;
using YrClean.Core.Services;
using CheckBox = System.Windows.Controls.CheckBox;
using MessageBox = System.Windows.MessageBox;

namespace YrClean.UI;

// Everything that starts with Windows. The checkbox switches an entry off or on; nothing is deleted.
public partial class StartupWindow : Window
{
    public StartupWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Reload();
    }

    private void Reload()
    {
        var rows = StartupManager.GetItems().Select(item => new StartupRow(item)).ToList();
        ItemsList.ItemsSource = rows;
        SummaryText.Text =
            $"{rows.Count(r => r.Enabled)} of {rows.Count} entries start with Windows. " +
            "Untick an entry to disable it - it stays in place and can be switched back on at any time.";
    }

    private void EnabledCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: StartupRow row } check)
            return;

        if (!StartupManager.SetEnabled(row.Item, check.IsChecked == true))
            MessageBox.Show(this, $"Could not change '{row.Name}'.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);

        Reload();
    }
}

public class StartupRow
{
    public StartupRow(StartupItem item) => Item = item;

    public StartupItem Item { get; }
    public string Name => Item.Name;
    public bool Enabled => Item.Enabled;
    public string Command => Item.Command;

    public string Details =>
        Item.Location + (string.IsNullOrWhiteSpace(Item.Publisher) ? "" : "  ·  " + Item.Publisher);

    public string Flags =>
        Item.FileMissing ? "executable not found"
        : Item.UnknownPublisher && Item.ExecutablePath is not null ? "unknown publisher"
        : "";
}
