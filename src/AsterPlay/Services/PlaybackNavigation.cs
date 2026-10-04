using AsterPlay.Models;

namespace AsterPlay.Services;

public sealed class PlaybackNavigationRequestedEventArgs : EventArgs
{
    public PlaybackNavigationRequestedEventArgs(EmbyClient client, PlaybackLaunch launch)
    {
        Client = client;
        Launch = launch;
    }

    public EmbyClient Client { get; }
    public PlaybackLaunch Launch { get; }
}

public static class PlaybackNavigation
{
    public static event EventHandler<PlaybackNavigationRequestedEventArgs>? Requested;

    public static void Open(EmbyClient client, PlaybackLaunch launch) =>
        Requested?.Invoke(null, new PlaybackNavigationRequestedEventArgs(client, launch));
}
