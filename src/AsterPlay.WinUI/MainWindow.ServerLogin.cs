using AsterPlay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AsterPlay.WinUI;

public sealed partial class MainWindow
{
    private bool _serverLoginDialogOpen;

    private async Task ShowServerLoginAsync(
        ServerProfile profile,
        string? initialMessage = null)
    {
        if (_serverLoginDialogOpen)
            return;

        var userNameBox = new TextBox
        {
            Header = "用户名",
            PlaceholderText = "Emby 用户名"
        };
        var passwordBox = new PasswordBox
        {
            Header = "密码",
            PasswordRevealMode = PasswordRevealMode.Peek
        };
        var validation = new TextBlock
        {
            Text = initialMessage ?? "",
            Foreground = new SolidColorBrush(
                Color.FromArgb(255, 214, 72, 72)),
            TextWrapping = TextWrapping.Wrap,
            Visibility = string.IsNullOrWhiteSpace(initialMessage)
                ? Visibility.Collapsed
                : Visibility.Visible
        };

        var content = new StackPanel
        {
            Spacing = 12,
            MinWidth = 360
        };
        content.Children.Add(new TextBlock
        {
            Text = profile.Url,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(userNameBox);
        content.Children.Add(passwordBox);
        content.Children.Add(validation);

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = $"登录 {profile.DisplayName}",
            Content = content,
            PrimaryButtonText = "登录",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        var authenticated = false;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            validation.Visibility = Visibility.Collapsed;

            if (string.IsNullOrWhiteSpace(userNameBox.Text))
            {
                validation.Text = "请输入用户名。";
                validation.Visibility = Visibility.Visible;
                return;
            }

            var deferral = args.GetDeferral();
            dialog.IsPrimaryButtonEnabled = false;
            userNameBox.IsEnabled = false;
            passwordBox.IsEnabled = false;

            try
            {
                var candidate = new EmbyClient();
                var session = await candidate.AuthenticateAsync(
                    profile.Url,
                    userNameBox.Text,
                    passwordBox.Password);

                string? serverName = null;
                try
                {
                    serverName = await candidate.GetServerNameAsync();
                }
                catch (Exception ex)
                {
                    PlaybackLog.Error("ServerNameLookup", ex);
                }

                _client.Restore(session);
                ServerProfileStore.EnsureExists(profile.Url);
                if (!string.IsNullOrWhiteSpace(serverName))
                {
                    ServerProfileStore.UpdateServerName(
                        profile.Url,
                        serverName);
                }

                InvalidateRetainedHome();
                _authenticated = true;
                authenticated = true;
                args.Cancel = false;
            }
            catch (Exception ex)
            {
                validation.Text = UserError.GetMessage(ex, "登录");
                validation.Visibility = Visibility.Visible;
            }
            finally
            {
                dialog.IsPrimaryButtonEnabled = true;
                userNameBox.IsEnabled = true;
                passwordBox.IsEnabled = true;
                deferral.Complete();
            }
        };

        _serverLoginDialogOpen = true;
        try
        {
            await dialog.ShowAsync();
        }
        finally
        {
            _serverLoginDialogOpen = false;
        }

        if (authenticated)
            ShowHome();
    }
}
