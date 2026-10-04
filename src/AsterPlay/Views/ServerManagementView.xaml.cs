using AsterPlay.Services;

namespace AsterPlay.Views;

public partial class ServerManagementView : UserControl
{
    public event EventHandler? DoneRequested;

    public ServerManagementView()
    {
        InitializeComponent();
        Reload();
    }

    private void Reload()
    {
        ServerList.ItemsSource = ServerProfileStore.Load();
    }

    private void ServerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ServerList.SelectedItem is not ServerProfile profile)
            return;

        NameBox.Text = profile.Name;
        UrlBox.Text = profile.Url;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UrlBox.Text))
            return;

        ServerProfileStore.AddOrUpdate(UrlBox.Text, NameBox.Text);
        Reload();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (ServerList.SelectedItem is not ServerProfile profile)
            return;

        ServerProfileStore.Remove(profile.Url);
        NameBox.Clear();
        UrlBox.Text = "http://127.0.0.1:8096";
        Reload();
    }

    private void Done_Click(object sender, RoutedEventArgs e) =>
        DoneRequested?.Invoke(this, EventArgs.Empty);
}
