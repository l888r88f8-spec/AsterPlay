using AsterPlay.Services;

namespace AsterPlay.Views;

public partial class LoginView : UserControl
{
    private readonly EmbyClient _client;
    public event EventHandler? LoginSucceeded;

    public LoginView(EmbyClient client, string? message = null)
    {
        _client = client;
        InitializeComponent();
        MessageBlock.Text = message ?? "";
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        LoginButton.IsEnabled = false;
        MessageBlock.Text = "";

        try
        {
            await _client.AuthenticateAsync(
                ServerBox.Text,
                UserBox.Text,
                PasswordBox.Password);
            LoginSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            MessageBlock.Text = ex.Message;
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }
}
