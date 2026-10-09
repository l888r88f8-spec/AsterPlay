using AsterPlay.Models;
using AsterPlay.Services;

namespace AsterPlay.WinUI;

public sealed partial class MainWindow
{
    internal EmbyClient ServerManagementClient => _client;

    internal void SetServerEditorMode(bool enabled) =>
        NavigationDock.Visibility = enabled
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

    internal void RequestServerSwitchFromManagement(
        ServerProfile profile) =>
        SwitchServer(profile);

    internal void CompleteServerLoginFromManagement(
        EmbySession session)
    {
        _client.Restore(session);
        _authenticated = true;
        InvalidateRetainedHome();
        ShowHome();
    }
}
