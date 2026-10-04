using System.Windows.Media;
using AsterPlay.Services;
using AsterPlay.Views;

namespace AsterPlay;

public partial class MainWindow : Window
{
    private readonly EmbyClient _client = new();

    public MainWindow()
    {
        InitializeComponent();
        FitToWorkArea();
        Loaded += OnLoaded;
        StateChanged += (_, _) => UpdateMaximizeButton();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void HomeNav_Click(object sender, RoutedEventArgs e) => ShowHome();

    private void LibraryNav_Click(object sender, RoutedEventArgs e) => ShowLibrary();

    private void UpdateMaximizeButton()
    {
        if (MaximizeButton is not null)
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private void FitToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;

        Width = Math.Min(Width, workArea.Width * 0.90);
        Height = Math.Min(Height, workArea.Height * 0.90);

        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
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
        SetNavigationVisible(false);

        var login = new LoginView(_client, message);
        login.LoginSucceeded += (_, _) => ShowHome();
        RootContent.Content = login;
    }

    private void ShowHome()
    {
        SetNavigationVisible(true);
        SetActiveNavigation(homeActive: true);

        var home = new HomeView(_client);
        home.LogoutRequested += (_, _) =>
        {
            AppStateStore.Clear();
            _client.Reset();
            ShowLogin();
        };

        RootContent.Content = home;
    }

    private void ShowLibrary()
    {
        SetNavigationVisible(true);
        SetActiveNavigation(homeActive: false);
        RootContent.Content = new LibraryView(_client);
    }

    private void SetNavigationVisible(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        HomeNavButton.Visibility = visibility;
        LibraryNavButton.Visibility = visibility;
    }

    private void SetActiveNavigation(bool homeActive)
    {
        HomeNavButton.Background = homeActive
            ? new SolidColorBrush(Color.FromArgb(0x30, 0x3E, 0xA6, 0xFF))
            : Brushes.Transparent;
        HomeNavButton.Foreground = homeActive
            ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(0xBF, 0xC4, 0xCE));

        LibraryNavButton.Background = !homeActive
            ? new SolidColorBrush(Color.FromArgb(0x30, 0x3E, 0xA6, 0xFF))
            : Brushes.Transparent;
        LibraryNavButton.Foreground = !homeActive
            ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(0xBF, 0xC4, 0xCE));
    }
}
