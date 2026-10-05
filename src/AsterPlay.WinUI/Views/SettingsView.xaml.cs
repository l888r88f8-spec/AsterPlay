using AsterPlay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AsterPlay.WinUI.Views;

public sealed partial class SettingsView : UserControl
{
    private bool _loading;

    public event EventHandler? LogoutRequested;

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

    private void SettingChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        var seconds = 3;
        if (AutoHideComboBox.SelectedItem is ComboBoxItem item)
            int.TryParse(item.Tag?.ToString(), out seconds);

        AppSettingsStore.Save(new AppSettings
        {
            RestoreSessionOnStartup = RestoreSessionCheckBox.IsChecked == true,
            PlayerControlsAutoHideSeconds = Math.Clamp(seconds, 2, 8)
        });
    }
}
