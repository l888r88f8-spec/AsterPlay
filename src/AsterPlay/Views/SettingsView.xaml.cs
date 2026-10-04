using AsterPlay.Services;

namespace AsterPlay.Views;

public partial class SettingsView : UserControl
{
    private bool _loading;

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

            foreach (var item in AutoHideComboBox.Items.OfType<ComboBoxItem>())
            {
                if (int.TryParse(item.Tag?.ToString(), out var seconds) &&
                    seconds == settings.PlayerControlsAutoHideSeconds)
                {
                    AutoHideComboBox.SelectedItem = item;
                    break;
                }
            }

            AutoHideComboBox.SelectedIndex = AutoHideComboBox.SelectedIndex < 0 ? 1 : AutoHideComboBox.SelectedIndex;
        }
        finally
        {
            _loading = false;
        }
    }

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
