using AsterPlay.Services;

namespace AsterPlay.Views;

public partial class LoginView : UserControl
{
    private readonly EmbyClient _client;

    public event EventHandler? LoginSucceeded;
    public event EventHandler? ManageServersRequested;

    public LoginView(EmbyClient client, string? message = null)
    {
        _client = client;
        InitializeComponent();

        var servers = ServerProfileStore.Load();
        ServerBox.ItemsSource = servers;
        ServerBox.Text = servers.FirstOrDefault()?.Url ?? "http://127.0.0.1:8096";
        MessageBlock.Text = message ?? "";
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        LoginButton.IsEnabled = false;
        MessageBlock.Text = "";

        try
        {
            var serverUrl = ServerBox.Text?.Trim() ?? "";
            await _client.AuthenticateAsync(
                serverUrl,
                UserBox.Text,
                PasswordBox.Password);

            ServerProfileStore.AddOrUpdate(serverUrl);
            LoginSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            MessageBlock.Text = UserError.GetMessage(ex, "登录");
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private void ManageServers_Click(object sender, RoutedEventArgs e) =>
        ManageServersRequested?.Invoke(this, EventArgs.Empty);
}
