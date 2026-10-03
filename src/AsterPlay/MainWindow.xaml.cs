using System.Windows.Input;
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
        StateChanged += (_, _) => UpdateMaximizeGlyph();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        UpdateMaximizeGlyph();
    }

    private void UpdateMaximizeGlyph()
    {
        if (MaximizeButton is not null)
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
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
