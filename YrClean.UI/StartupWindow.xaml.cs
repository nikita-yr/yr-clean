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
        SummaryText.Text = L.T("Start_Summary", rows.Count(r => r.Enabled), rows.Count);
    }

    private void EnabledCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: StartupRow row } check)
            return;

        if (!StartupManager.SetEnabled(row.Item, check.IsChecked == true))
            MessageBox.Show(this, L.T("Start_Failed", row.Name), Title, MessageBoxButton.OK, MessageBoxImage.Warning);

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
        L.T(Item.Kind switch
        {
            StartupKind.StartupFolder => "Start_KindFolder",
            StartupKind.ScheduledTask => "Start_KindTask",
            _ => "Start_KindRegistry"
        }) + ": " + Item.Location + (string.IsNullOrWhiteSpace(Item.Publisher) ? "" : "  ·  " + Item.Publisher);

    public string Flags =>
        Item.FileMissing ? L.T("Start_Missing")
        : Item.UnknownPublisher && Item.ExecutablePath is not null ? L.T("Start_UnknownPublisher")
        : "";
}
