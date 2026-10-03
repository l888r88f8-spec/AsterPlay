using AsterPlay.Services;
using AsterPlay.Views;

namespace AsterPlay;

public partial class MainWindow : Window
{
    private readonly EmbyClient _client = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var session = AppStateStore.Load();
        if (session is not null)
        {
            try
            {
                _client.Restore(session);
                await _client.GetViewsAsync();
                ShowHome();
                return;
            }
            catch
            {
                AppStateStore.Clear();
                _client.Reset();
            }
        }

        ShowLogin();
    }

    private void ShowLogin(string? message = null)
    {
        var login = new LoginView(_client, message);
        login.LoginSucceeded += (_, _) => ShowHome();
        RootContent.Content = login;
    }

    private void ShowHome()
    {
        var home = new HomeView(_client);
        home.LogoutRequested += (_, _) =>
        {
            AppStateStore.Clear();
            _client.Reset();
            ShowLogin();
        };
        RootContent.Content = home;
    }
}
