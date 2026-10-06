using AsterPlay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AsterPlay.WinUI.Views;

public sealed partial class LoginView : UserControl
{
    private readonly EmbyClient _client;

    public event EventHandler? LoginSucceeded;
    public event EventHandler? ManageServersRequested;

    public LoginView(
        EmbyClient client,
        string? message = null,
        string? preferredServerUrl = null)
    {
        _client = client;
        InitializeComponent();

        ReloadServers(preferredServerUrl);
        ShowMessage(message);
    }

    private void ReloadServers(string? preferredServerUrl = null)
    {
        var servers = ServerProfileStore.Load();
        SavedServerBox.ItemsSource = servers;

        var preferred = (preferredServerUrl ?? "").Trim().TrimEnd('/');
        var selectedIndex = 0;

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            for (var i = 0; i < servers.Count; i++)
            {
                var candidate = (servers[i].Url ?? "").Trim().TrimEnd('/');
                if (string.Equals(
                        candidate,
                        preferred,
                        StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                    break;
                }
            }
        }

        if (servers.Count > 0)
        {
            SavedServerBox.SelectedIndex = Math.Clamp(
                selectedIndex,
                0,
                servers.Count - 1);
            ServerUrlBox.Text = servers[SavedServerBox.SelectedIndex].Url;
        }
        else if (!string.IsNullOrWhiteSpace(preferredServerUrl))
        {
            ServerUrlBox.Text = preferredServerUrl;
        }
    }

    private void SavedServerBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SavedServerBox.SelectedItem is ServerProfile profile)
            ServerUrlBox.Text = profile.Url;
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        LoginButton.IsEnabled = false;
        ShowMessage(null);

        try
        {
            var serverUrl = ServerUrlBox.Text.Trim();
            await _client.AuthenticateAsync(serverUrl, UserNameBox.Text, PasswordBox.Password);

            string? serverName = null;
            try
            {
                serverName = await _client.GetServerNameAsync();
            }
            catch (Exception ex)
            {
                PlaybackLog.Error("ServerNameLookup", ex);
            }

            if (!string.IsNullOrWhiteSpace(serverName))
                ServerProfileStore.UpdateServerName(serverUrl, serverName);
            else if (!ServerProfileStore.Load().Any(profile =>
                         string.Equals(
                             profile.Url.Trim().TrimEnd('/'),
                             serverUrl.Trim().TrimEnd('/'),
                             StringComparison.OrdinalIgnoreCase)))
                ServerProfileStore.AddOrUpdate(serverUrl, "");

            LoginSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ShowMessage(UserError.GetMessage(ex, "登录"));
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private void ManageServers_Click(object sender, RoutedEventArgs e) =>
        ManageServersRequested?.Invoke(this, EventArgs.Empty);

    private void ShowMessage(string? message)
    {
        MessageBlock.Text = message ?? "";
        MessageBlock.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
