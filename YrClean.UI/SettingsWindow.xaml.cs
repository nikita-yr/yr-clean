using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using YrClean.Core.Models;
using YrClean.Core.Services;

namespace YrClean.UI;

public partial class SettingsWindow : Window
{
    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private readonly CleanSettings _settings;

    public SettingsWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => UseDarkMode();
        _settings = SettingsService.Load();

        ScheduleEnabledCheck.IsChecked = _settings.ScheduleEnabled;
        FrequencyCombo.SelectedIndex = (int)_settings.Frequency; // Hourly=0, Daily=1, Weekly=2
        DayCombo.SelectedIndex = (int)_settings.WeeklyDay == 0 ? 6 : (int)_settings.WeeklyDay - 1;
        NotifyCheck.IsChecked = _settings.NotifyOnComplete;
        IncludeAutoDiscoveredCheck.IsChecked = _settings.IncludeAutoDiscoveredInScheduledRun;

        var items = AgeThresholdCombo.Items.Cast<ComboBoxItem>().ToArray();
        var matchIndex = Array.FindIndex(items, item => (string)item.Tag == _settings.MinAgeDays.ToString());

        if (matchIndex >= 0)
        {
            AgeThresholdCombo.SelectedIndex = matchIndex;
        }
        else
        {
            AgeThresholdCombo.SelectedIndex = items.Length - 1;
            CustomDaysTextBox.Text = _settings.MinAgeDays.ToString();
        }

        UpdateFieldVisibility();
        UpdateCustomDaysVisibility();
    }

    private void UseDarkMode()
    {
        var handle = new WindowInteropHelper(this).Handle;
        int useDarkMode = 1;
        DwmSetWindowAttribute(handle, 20, ref useDarkMode, sizeof(int));
    }

    private void FrequencyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateFieldVisibility();

    private void AgeThresholdCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateCustomDaysVisibility();

    private void UpdateFieldVisibility()
    {
        bool isWeekly = FrequencyCombo.SelectedIndex == 2;

        DayLabel.Visibility = isWeekly ? Visibility.Visible : Visibility.Collapsed;
        DayCombo.Visibility = isWeekly ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateCustomDaysVisibility()
    {
        bool isCustom = (AgeThresholdCombo.SelectedItem as ComboBoxItem)?.Tag as string == "custom";
        CustomDaysLabel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        CustomDaysTextBox.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedTag = (AgeThresholdCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        int minAgeDays;

        if (selectedTag == "custom")
        {
            if (!int.TryParse(CustomDaysTextBox.Text, out minAgeDays) || minAgeDays < 1)
            {
                System.Windows.MessageBox.Show(this, "Custom days must be a positive whole number.", "YrClean",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }
        else
        {
            minAgeDays = int.Parse(selectedTag!);
        }

        _settings.MinAgeDays = minAgeDays;
        _settings.ScheduleEnabled = ScheduleEnabledCheck.IsChecked == true;
        _settings.Frequency = (ScheduleFrequency)FrequencyCombo.SelectedIndex;
        _settings.NotifyOnComplete = NotifyCheck.IsChecked == true;
        _settings.IncludeAutoDiscoveredInScheduledRun = IncludeAutoDiscoveredCheck.IsChecked == true;

        var dayNames = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                               DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        _settings.WeeklyDay = dayNames[DayCombo.SelectedIndex];

        SettingsService.Save(_settings);

        var exePath = Environment.ProcessPath!;
        var vbsPath = System.IO.Path.Combine(AppContext.BaseDirectory, "invisible.vbs");
        var ok = TaskSchedulerService.Register(_settings, exePath, vbsPath);

        if (!ok)
        {
            System.Windows.MessageBox.Show(this, "Failed to update the scheduled task.", "YrClean",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return; // keep the window open so the user can retry
        }

        var summary = _settings.ScheduleEnabled
            ? "Settings saved and the scheduled task was updated."
            : "Settings saved. Scheduled auto-clean is now disabled.";

        System.Windows.MessageBox.Show(this, summary, "YrClean", MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }
}