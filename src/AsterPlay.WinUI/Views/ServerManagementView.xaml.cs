using AsterPlay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AsterPlay.WinUI.Views;

public sealed partial class ServerManagementView : UserControl
{
    private readonly EmbyClient _serverLookupClient = new();

    public event EventHandler? DoneRequested;

    public ServerManagementView()
    {
        InitializeComponent();
        Reload();
    }

    private void Reload()
    {
        ServerList.ItemsSource = ServerProfileStore.Load();
    }

    private void ServerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ServerList.SelectedItem is not ServerProfile profile)
            return;

        NameBox.Text = profile.Name;
        UrlBox.Text = profile.Url;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UrlBox.Text))
            return;

        var url = UrlBox.Text.Trim();
        var customName = NameBox.Text.Trim();

        if (sender is Button button)
            button.IsEnabled = false;

        try
        {
            ServerProfileStore.AddOrUpdate(url, customName);

            // Keep the server's own name cached as a fallback. A custom name,
            // when present, still wins in DisplayName.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            try
            {
                var serverName = await _serverLookupClient.GetServerNameAsync(
                    url,
                    timeout.Token);
                ServerProfileStore.UpdateServerName(url, serverName);
            }
            catch (Exception ex)
            {
                PlaybackLog.Error("ServerNameLookup", ex);
            }

            Reload();
        }
        finally
        {
            if (sender is Button saveButton)
                saveButton.IsEnabled = true;
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (ServerList.SelectedItem is not ServerProfile profile)
            return;

        ServerProfileStore.Remove(profile.Url);
        NameBox.Text = "";
        UrlBox.Text = "http://127.0.0.1:8096";
        Reload();
    }

    private void Done_Click(object sender, RoutedEventArgs e) =>
        DoneRequested?.Invoke(this, EventArgs.Empty);
}
