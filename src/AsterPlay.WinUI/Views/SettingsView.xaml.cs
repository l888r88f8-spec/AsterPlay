using AsterPlay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AsterPlay.WinUI.Views;

public sealed partial class SettingsView : UserControl
{
    private bool _loading;

    public event EventHandler? LogoutRequested;
    public event EventHandler<string>? ThemeModeChanged;

    public SettingsView()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        _loading = true;

        try
        {
            var settings = AppSettingsStore.Load();
            RestoreSessionCheckBox.IsChecked = settings.RestoreSessionOnStartup;

            var themeItems = ThemeModeComboBox.Items.OfType<ComboBoxItem>().ToArray();
            var themeIndex = Array.FindIndex(
                themeItems,
                item => string.Equals(
                    item.Tag?.ToString(),
                    settings.ThemeMode,
                    StringComparison.OrdinalIgnoreCase));
            ThemeModeComboBox.SelectedIndex = themeIndex >= 0 ? themeIndex : 0;

            var items = AutoHideComboBox.Items.OfType<ComboBoxItem>().ToArray();
            var selected = Array.FindIndex(
                items,
                item => int.TryParse(item.Tag?.ToString(), out var seconds) &&
                        seconds == settings.PlayerControlsAutoHideSeconds);

            AutoHideComboBox.SelectedIndex = selected >= 0 ? selected : 1;
        }
        finally
        {
            _loading = false;
        }
    }

    private void Logout_Click(object sender, RoutedEventArgs e) =>
        LogoutRequested?.Invoke(this, EventArgs.Empty);

    private void SettingChanged(object sender, RoutedEventArgs e) =>
        SaveSettings(themeChanged: false);

    private void ThemeMode_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e) =>
        SaveSettings(themeChanged: true);

    private void SaveSettings(bool themeChanged)
    {
        if (_loading)
            return;

        var seconds = 3;
        if (AutoHideComboBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out var parsed))
        {
            seconds = parsed;
        }

        var mode = (ThemeModeComboBox.SelectedItem as ComboBoxItem)?
            .Tag?.ToString() ?? "system";

        AppSettingsStore.Save(new AppSettings
        {
            RestoreSessionOnStartup = RestoreSessionCheckBox.IsChecked == true,
            PlayerControlsAutoHideSeconds = Math.Clamp(seconds, 2, 8),
            ThemeMode = mode
        });

        if (themeChanged)
            ThemeModeChanged?.Invoke(this, mode);
    }
}
