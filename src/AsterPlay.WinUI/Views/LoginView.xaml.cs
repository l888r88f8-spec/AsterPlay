using AsterPlay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AsterPlay.WinUI.Views;

public sealed partial class LoginView : UserControl
{
    private readonly EmbyClient _client;

    public event EventHandler? LoginSucceeded;
    public event EventHandler? ManageServersRequested;

    public LoginView(EmbyClient client, string? message = null)
    {
        _client = client;
        InitializeComponent();

        ReloadServers();
        ShowMessage(message);
    }

    private void ReloadServers()
    {
        var servers = ServerProfileStore.Load();
        SavedServerBox.ItemsSource = servers;

        if (servers.Count > 0)
        {
            SavedServerBox.SelectedIndex = 0;
            ServerUrlBox.Text = servers[0].Url;
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

            ServerProfileStore.AddOrUpdate(serverUrl, serverName);
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
