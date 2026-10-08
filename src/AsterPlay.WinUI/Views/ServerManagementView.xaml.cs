using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AsterPlay.Services;
using AsterPlay.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AsterPlay.WinUI.Views;

public sealed partial class ServerManagementView : UserControl
{
    private readonly EmbyClient _client;
    private readonly EmbyClient _serverLookupClient = new();
    private readonly ObservableCollection<ServerCardItem> _servers = [];
    private bool _initialRefreshCompleted;
    private bool _refreshing;
    private bool _sortDescending;
    private bool _compactLayout;

    public event EventHandler? DoneRequested;
    public event EventHandler<ServerProfile>? ServerSwitchRequested;

    public ServerManagementView()
        : this(
            ResolveMainClient(),
            showBackButton: ResolveShowBackButton())
    {
    }

    public ServerManagementView(
        EmbyClient client,
        bool showBackButton = false)
    {
        _client = client;
        InitializeComponent();

        BackButton.Visibility = showBackButton
            ? Visibility.Visible
            : Visibility.Collapsed;

        ServerCards.ItemsSource = _servers;
        Reload();
    }

    private static EmbyClient ResolveMainClient()
    {
        if (Application.Current is App app &&
            app.HostWindow is { } mainWindow)
        {
            return mainWindow.ServerManagementClient;
        }

        var client = new EmbyClient();
        var session = AppStateStore.Load();
        if (session is not null)
            client.Restore(session);

        return client;
    }

    private static bool ResolveShowBackButton() =>
        Application.Current is not App app ||
        app.HostWindow is null ||
        app.HostWindow.ServerManagementNeedsBackButton;

    private async void ServerManagementView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_initialRefreshCompleted)
            return;

        _initialRefreshCompleted = true;
        await RefreshAllServersAsync();
    }

    private void Reload()
    {
        var currentUrl = NormalizeUrl(_client.ServerUrl);
        var profiles = ServerProfileStore.Load();

        var items = profiles
            .Select(profile =>
            {
                var isCurrent = string.Equals(
                    NormalizeUrl(profile.Url),
                    currentUrl,
                    StringComparison.OrdinalIgnoreCase);

                return new ServerCardItem(
                    profile,
                    isCurrent,
                    isCurrent && _client.IsAuthenticated
                        ? _client.BuildCurrentUserPrimaryUrl(160)
                        : "");
            })
            .ToList();

        _servers.Clear();
        foreach (var item in SortItems(items))
            _servers.Add(item);

        ApplyCardMetrics(
            Root.ActualWidth);

        var hasServers = _servers.Count > 0;
        ServerCards.Visibility = hasServers
            ? Visibility.Visible
            : Visibility.Collapsed;
        EmptyState.Visibility = hasServers
            ? Visibility.Collapsed
            : Visibility.Visible;
        RefreshButton.Visibility = hasServers
            ? Visibility.Visible
            : Visibility.Collapsed;
        FooterText.Visibility = hasServers
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private IEnumerable<ServerCardItem> SortItems(
        IEnumerable<ServerCardItem> source)
    {
        var currentFirst = source
            .OrderByDescending(item => item.IsCurrent);

        return _sortDescending
            ? currentFirst.ThenByDescending(
                item => item.DisplayName,
                StringComparer.CurrentCultureIgnoreCase)
            : currentFirst.ThenBy(
                item => item.DisplayName,
                StringComparer.CurrentCultureIgnoreCase);
    }

    private async Task RefreshAllServersAsync()
    {
        if (_refreshing || _servers.Count == 0)
            return;

        _refreshing = true;
        RefreshButton.IsEnabled = false;
        RefreshIcon.Visibility = Visibility.Collapsed;
        RefreshProgress.Visibility = Visibility.Visible;
        RefreshProgress.IsActive = true;
        RefreshLabel.Text = "正在刷新…";

        try
        {
            await Task.WhenAll(
                _servers.Select(RefreshServerCardAsync));
        }
        finally
        {
            RefreshProgress.IsActive = false;
            RefreshProgress.Visibility = Visibility.Collapsed;
            RefreshIcon.Visibility = Visibility.Visible;
            RefreshLabel.Text = "刷新服务器数据";
            RefreshButton.IsEnabled = true;
            _refreshing = false;
        }
    }

    private async Task RefreshServerCardAsync(ServerCardItem item)
    {
        try
        {
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(4));

            var serverName =
                await _serverLookupClient.GetServerNameAsync(
                    item.Url,
                    timeout.Token);

            if (!string.IsNullOrWhiteSpace(serverName))
            {
                ServerProfileStore.UpdateServerName(
                    item.Url,
                    serverName);
                item.SetServerName(serverName);
            }

            item.SetOnline();

            if (item.IsCurrent && _client.IsAuthenticated)
                await RefreshCurrentServerMediaCountsAsync(item);
            else
                item.MetaText = "点击切换到此服务器";
        }
        catch (Exception ex)
        {
            item.SetOffline();
            PlaybackLog.Error(
                "ServerManagementRefresh",
                ex);
        }
    }

    private async Task RefreshCurrentServerMediaCountsAsync(
        ServerCardItem item)
    {
        try
        {
            var moviesTask = _client.GetLibraryItemsAsync(
                parentId: null,
                searchTerm: null,
                includeItemTypes: "Movie",
                year: null,
                sortBy: "SortName",
                sortOrder: "Ascending",
                favoriteOnly: false,
                startIndex: 0,
                limit: 1);

            var seriesTask = _client.GetLibraryItemsAsync(
                parentId: null,
                searchTerm: null,
                includeItemTypes: "Series",
                year: null,
                sortBy: "SortName",
                sortOrder: "Ascending",
                favoriteOnly: false,
                startIndex: 0,
                limit: 1);

            await Task.WhenAll(
                moviesTask,
                seriesTask);

            item.MetaText =
                $"电影 {moviesTask.Result.TotalRecordCount} · 剧集 {seriesTask.Result.TotalRecordCount}";
        }
        catch (Exception ex)
        {
            item.MetaText = "当前服务器";
            PlaybackLog.Error(
                "ServerManagementMediaCount",
                ex);
        }
    }

    private async void Refresh_Click(
        object sender,
        RoutedEventArgs e) =>
        await RefreshAllServersAsync();

    private void ServerCard_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not ServerCardItem item ||
            item.IsCurrent)
        {
            return;
        }

        if (ServerSwitchRequested is not null)
        {
            ServerSwitchRequested.Invoke(
                this,
                item.NavigationProfile);
            return;
        }

        if (Application.Current is App app &&
            app.HostWindow is { } mainWindow)
        {
            mainWindow.RequestServerSwitchFromManagement(
                item.NavigationProfile);
        }
    }

    private void More_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not ServerCardItem item)
        {
            return;
        }

        var flyout = new MenuFlyout();

        var editItem = new MenuFlyoutItem
        {
            Text = "编辑服务器"
        };
        editItem.Click += async (_, _) =>
            await ShowEditorAsync(item);

        var deleteItem = new MenuFlyoutItem
        {
            Text = "删除服务器"
        };
        deleteItem.Click += async (_, _) =>
            await DeleteServerAsync(item);

        flyout.Items.Add(editItem);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(deleteItem);
        flyout.ShowAt(button);
    }

    private async void Add_Click(
        object sender,
        RoutedEventArgs e) =>
        await ShowEditorAsync(null);

    private async Task ShowEditorAsync(
        ServerCardItem? item)
    {
        var nameBox = new TextBox
        {
            Header = "名称（可选）",
            PlaceholderText = "留空时自动使用服务器名称",
            Text = item?.CustomName ?? ""
        };

        var urlBox = new TextBox
        {
            Header = "服务器地址",
            PlaceholderText = "https://example.com",
            Text = item?.Url ?? "http://127.0.0.1:8096"
        };

        var validation = new TextBlock
        {
            Text = "",
            Foreground = new SolidColorBrush(
                Color.FromArgb(255, 214, 72, 72)),
            FontSize = 12,
            Visibility = Visibility.Collapsed
        };

        var content = new StackPanel
        {
            Spacing = 12,
            MinWidth = 380
        };
        content.Children.Add(nameBox);
        content.Children.Add(urlBox);
        content.Children.Add(validation);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = item is null
                ? "添加服务器"
                : "编辑服务器",
            Content = content,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        var acceptedUrl = "";
        var acceptedName = "";

        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!TryNormalizeServerUrl(
                    urlBox.Text,
                    out var normalized))
            {
                args.Cancel = true;
                validation.Text =
                    "请输入以 http:// 或 https:// 开头的有效服务器地址。";
                validation.Visibility = Visibility.Visible;
                return;
            }

            acceptedUrl = normalized;
            acceptedName = nameBox.Text.Trim();
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary ||
            string.IsNullOrWhiteSpace(acceptedUrl))
        {
            return;
        }

        if (item is not null &&
            !string.Equals(
                NormalizeUrl(item.Url),
                acceptedUrl,
                StringComparison.OrdinalIgnoreCase))
        {
            ServerProfileStore.Remove(item.Url);
        }

        ServerProfileStore.AddOrUpdate(
            acceptedUrl,
            acceptedName);

        try
        {
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(4));
            var serverName =
                await _serverLookupClient.GetServerNameAsync(
                    acceptedUrl,
                    timeout.Token);
            ServerProfileStore.UpdateServerName(
                acceptedUrl,
                serverName);
        }
        catch (Exception ex)
        {
            PlaybackLog.Error(
                "ServerNameLookup",
                ex);
        }

        Reload();
        await RefreshAllServersAsync();
    }

    private async Task DeleteServerAsync(
        ServerCardItem item)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "删除服务器？",
            Content =
                $"将从 AsterPlay 中移除“{item.DisplayName}”。登录凭据不会被保存或删除。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        ServerProfileStore.Remove(item.Url);
        Reload();
    }

    private void Sort_Click(
        object sender,
        RoutedEventArgs e)
    {
        _sortDescending = !_sortDescending;

        var reordered = SortItems(
            _servers.ToList()).ToList();

        _servers.Clear();
        foreach (var item in reordered)
            _servers.Add(item);

        ToolTipService.SetToolTip(
            SortButton,
            _sortDescending
                ? "名称降序"
                : "名称升序");
    }

    private void Layout_Click(
        object sender,
        RoutedEventArgs e)
    {
        _compactLayout = !_compactLayout;

        ApplyCardMetrics(
            Root.ActualWidth);

        ToolTipService.SetToolTip(
            LayoutButton,
            _compactLayout
                ? "切换为宽卡片"
                : "切换为紧凑卡片");
    }

    private void ApplyCardMetrics(
        double availableWidth)
    {
        var cardWidth = _compactLayout
            ? 300d
            : 330d;
        var cardHeight = _compactLayout
            ? 184d
            : 216d;

        if (availableWidth > 0 &&
            availableWidth < 520)
        {
            cardWidth = Math.Max(
                260,
                availableWidth - 44);
            cardHeight = Math.Round(
                cardWidth * (216d / 330d));
        }

        if (ServerCards.Layout is UniformGridLayout layout)
        {
            layout.MinItemWidth = cardWidth;
            layout.MinItemHeight = cardHeight;
            layout.MinColumnSpacing = 18;
            layout.MinRowSpacing = 18;
        }

        foreach (var item in _servers)
        {
            item.CardWidth = cardWidth;
            item.CardHeight = cardHeight;
        }
    }

    private void Back_Click(
        object sender,
        RoutedEventArgs e) =>
        DoneRequested?.Invoke(
            this,
            EventArgs.Empty);

    private void Root_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        var horizontal =
            e.NewSize.Width < 760
                ? 22
                : e.NewSize.Width < 1100
                    ? 34
                    : 48;

        PageContent.Padding = new Thickness(
            horizontal,
            28,
            horizontal,
            124);

        PageTitle.FontSize =
            e.NewSize.Width < 760
                ? 34
                : 40;

        ApplyCardMetrics(
            e.NewSize.Width);
    }

    private static bool TryNormalizeServerUrl(
        string? value,
        out string normalized)
    {
        normalized = NormalizeUrl(value);

        if (!Uri.TryCreate(
                normalized,
                UriKind.Absolute,
                out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttp ||
               uri.Scheme == Uri.UriSchemeHttps;
    }

    private static string NormalizeUrl(
        string? value) =>
        (value ?? "")
            .Trim()
            .TrimEnd('/');

    public sealed class ServerCardItem :
        INotifyPropertyChanged
    {
        private string _serverName;
        private string _statusText;
        private Brush _statusBrush;
        private string _metaText;
        private double _cardWidth = 330;
        private double _cardHeight = 216;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ServerCardItem(
            ServerProfile profile,
            bool isCurrent,
            string avatarUrl)
        {
            CustomName = profile.Name;
            Url = profile.Url;
            AvatarUrl = avatarUrl;
            _serverName = profile.ServerName;
            IsCurrent = isCurrent;
            CardBorderBrush = new SolidColorBrush(
                isCurrent
                    ? Color.FromArgb(255, 22, 135, 233)
                    : Color.FromArgb(54, 128, 136, 148));
            CardBorderThickness = new Thickness(
                isCurrent ? 2 : 1);

            _statusText = isCurrent
                ? "当前服务器"
                : "等待检查";
            _statusBrush = new SolidColorBrush(
                Color.FromArgb(
                    255,
                    156,
                    163,
                    174));
            _metaText = isCurrent
                ? "正在读取媒体数量…"
                : "已保存的 Emby 服务器";
        }

        public string CustomName { get; }

        public string Url { get; }

        public string AvatarUrl { get; }

        public bool IsCurrent { get; }

        public Brush CardBorderBrush { get; }

        public Thickness CardBorderThickness { get; }

        public double CardWidth
        {
            get => _cardWidth;
            set
            {
                if (Math.Abs(_cardWidth - value) < 0.1)
                    return;

                _cardWidth = value;
                OnPropertyChanged();
            }
        }

        public double CardHeight
        {
            get => _cardHeight;
            set
            {
                if (Math.Abs(_cardHeight - value) < 0.1)
                    return;

                _cardHeight = value;
                OnPropertyChanged();
            }
        }

        public string DisplayName =>
            !string.IsNullOrWhiteSpace(CustomName)
                ? CustomName
                : !string.IsNullOrWhiteSpace(_serverName)
                    ? _serverName
                    : Url;

        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (_statusText == value)
                    return;

                _statusText = value;
                OnPropertyChanged();
            }
        }

        public Brush StatusBrush
        {
            get => _statusBrush;
            private set
            {
                if (ReferenceEquals(
                        _statusBrush,
                        value))
                {
                    return;
                }

                _statusBrush = value;
                OnPropertyChanged();
            }
        }

        public string MetaText
        {
            get => _metaText;
            set
            {
                if (_metaText == value)
                    return;

                _metaText = value;
                OnPropertyChanged();
            }
        }

        public ServerProfile NavigationProfile =>
            new(CustomName, Url)
            {
                ServerName = _serverName
            };

        public void SetServerName(
            string serverName)
        {
            serverName = serverName.Trim();
            if (string.IsNullOrWhiteSpace(serverName) ||
                string.Equals(
                    _serverName,
                    serverName,
                    StringComparison.Ordinal))
            {
                return;
            }

            _serverName = serverName;
            OnPropertyChanged(
                nameof(DisplayName));
        }

        public void SetOnline()
        {
            StatusText = IsCurrent
                ? "当前 · 刚刚"
                : "刚刚";
            StatusBrush = new SolidColorBrush(
                Color.FromArgb(
                    255,
                    67,
                    199,
                    82));
        }

        public void SetOffline()
        {
            StatusText = "离线";
            StatusBrush = new SolidColorBrush(
                Color.FromArgb(
                    255,
                    156,
                    163,
                    174));
            MetaText = "服务器暂时不可达";
        }

        private void OnPropertyChanged(
            [CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(name));
    }
}
