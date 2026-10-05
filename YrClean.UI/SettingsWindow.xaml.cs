using System.IO;
using System.Windows;
using System.Windows.Controls;
using YrClean.Core.Models;
using YrClean.Core.Services;
using MessageBox = System.Windows.MessageBox;

namespace YrClean.UI;

public partial class SettingsWindow : Window
{
    private const string CustomAgeTag = "custom";
    private const string DialogTitle = "YrClean";

    // Matches the item order of DayCombo (week starts on Monday)
    private static readonly DayOfWeek[] DayComboOrder =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    };

    private readonly CleanSettings _settings;

    public SettingsWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        _settings = SettingsService.Load();

        ScheduleEnabledCheck.IsChecked = _settings.ScheduleEnabled;
        FrequencyCombo.SelectedIndex = (int)_settings.Frequency; // FrequencyCombo items follow ScheduleFrequency order
        DayCombo.SelectedIndex = Array.IndexOf(DayComboOrder, _settings.WeeklyDay);
        NotifyCheck.IsChecked = _settings.NotifyOnComplete;
        IncludeAutoDiscoveredCheck.IsChecked = _settings.IncludeAutoDiscoveredInScheduledRun;
        SelectAgeThreshold(_settings.MinAgeDays);

        UpdateFieldVisibility();
        UpdateCustomDaysVisibility();
    }

    private string? SelectedAgeTag => (AgeThresholdCombo.SelectedItem as ComboBoxItem)?.Tag as string;

    private void SelectAgeThreshold(int minAgeDays)
    {
        var items = AgeThresholdCombo.Items.Cast<ComboBoxItem>().ToArray();
        var matchIndex = Array.FindIndex(items, item => (string)item.Tag == minAgeDays.ToString());

        if (matchIndex >= 0)
        {
            AgeThresholdCombo.SelectedIndex = matchIndex;
        }
        else
        {
            AgeThresholdCombo.SelectedIndex = items.Length - 1;
            CustomDaysTextBox.Text = minAgeDays.ToString();
        }
    }

    private void FrequencyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateFieldVisibility();

    private void AgeThresholdCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateCustomDaysVisibility();

    private void UpdateFieldVisibility()
    {
        bool isWeekly = FrequencyCombo.SelectedIndex == (int)ScheduleFrequency.Weekly;
        SetVisible(isWeekly, DayLabel, DayCombo);
    }

    private void UpdateCustomDaysVisibility()
    {
        bool isCustom = SelectedAgeTag == CustomAgeTag;
        SetVisible(isCustom, CustomDaysLabel, CustomDaysTextBox);
    }

    private static void SetVisible(bool visible, params UIElement[] elements)
    {
        foreach (var element in elements)
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool TryReadMinAgeDays(out int minAgeDays)
    {
        var selectedTag = SelectedAgeTag;
        if (selectedTag != CustomAgeTag)
        {
            minAgeDays = int.Parse(selectedTag!);
            return true;
        }

        return int.TryParse(CustomDaysTextBox.Text, out minAgeDays) && minAgeDays >= 1;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadMinAgeDays(out var minAgeDays))
        {
            ShowMessage("Custom days must be a positive whole number.", MessageBoxImage.Error);
            return;
        }

        _settings.MinAgeDays = minAgeDays;
        _settings.ScheduleEnabled = ScheduleEnabledCheck.IsChecked == true;
        _settings.Frequency = (ScheduleFrequency)FrequencyCombo.SelectedIndex;
        _settings.WeeklyDay = DayComboOrder[DayCombo.SelectedIndex];
        _settings.NotifyOnComplete = NotifyCheck.IsChecked == true;
        _settings.IncludeAutoDiscoveredInScheduledRun = IncludeAutoDiscoveredCheck.IsChecked == true;

        SettingsService.Save(_settings);

        var exePath = Environment.ProcessPath!;
        var vbsPath = Path.Combine(AppContext.BaseDirectory, "invisible.vbs");
        if (!TaskSchedulerService.Register(_settings, exePath, vbsPath))
        {
            ShowMessage("Failed to update the scheduled task.", MessageBoxImage.Error);
            return; // keep the window open so the user can retry
        }

        var summary = _settings.ScheduleEnabled
            ? "Settings saved and the scheduled task was updated."
            : "Settings saved. Scheduled auto-clean is now disabled.";

        ShowMessage(summary, MessageBoxImage.Information);
        Close();
    }

    private void ShowMessage(string message, MessageBoxImage image) =>
        MessageBox.Show(this, message, DialogTitle, MessageBoxButton.OK, image);
}
