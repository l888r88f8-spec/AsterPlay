using AsterPlay.Services;

namespace AsterPlay.WinUI;

public sealed partial class MainWindow
{
    internal EmbyClient ServerManagementClient => _client;

    internal bool ServerManagementNeedsBackButton =>
        !_authenticated;

    internal void RequestServerSwitchFromManagement(
        ServerProfile profile) =>
        SwitchServer(profile);
}
