using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;

namespace AsterPlay.Views;

public partial class PlayerControlsWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    private static readonly double[] Speeds = [0.5, 0.75, 1.0, 1.25, 1.5, 2.0];

    private bool _updatingUi;

    public event Action? UserActivity;
    public event Action? TogglePauseRequested;
    public event Action? BackRequested;
    public event Action? ForwardRequested;
    public event Action? MuteRequested;
    public event Action? AudioRequested;
    public event Action? SubtitleRequested;
    public event Action? FullscreenRequested;
    public event Action<double>? SeekRequested;
    public event Action<double>? VolumeChanged;
    public event Action<double>? SpeedChanged;

    public PlayerControlsWindow(string title)
    {
        InitializeComponent();
        TitleBlock.Text = title;
        SpeedComboBox.ItemsSource = Speeds.Select(x => $"{x:0.##}×").ToArray();
        SpeedComboBox.SelectedIndex = 2;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var styles = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(styles | WsExNoActivate | WsExToolWindow));
    }

    public void UpdateState(
        double position,
        double duration,
        bool paused,
        bool buffering,
        double volume,
        double speed)
    {
        _updatingUi = true;
        try
        {
            if (duration > 0)
            {
                PositionSlider.Maximum = duration;
                PositionSlider.Value = Math.Clamp(position, 0, duration);
            }

            CurrentTimeBlock.Text = FormatTime(position);
            DurationBlock.Text = duration > 0 ? FormatTime(duration) : "--:--";
            PauseButton.Content = paused ? "播放" : "暂停";
            MuteButton.Content = volume <= 0.01 ? "取消静音" : "静音";
            StatusBlock.Text = buffering ? "缓冲中…" : paused ? "已暂停" : "";

            VolumeSlider.Value = Math.Clamp(volume, 0, 100);

            var speedIndex = Array.FindIndex(Speeds, x => Math.Abs(x - speed) < 0.01);
            if (speedIndex >= 0 && SpeedComboBox.SelectedIndex != speedIndex)
                SpeedComboBox.SelectedIndex = speedIndex;
        }
        finally
        {
            _updatingUi = false;
        }
    }

    public void SetStatus(string text) => StatusBlock.Text = text;

    private void ControlsWindow_MouseMove(object sender, MouseEventArgs e) => UserActivity?.Invoke();

    private void PositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        UserActivity?.Invoke();
        if (!_updatingUi)
            SeekRequested?.Invoke(PositionSlider.Value);
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UserActivity?.Invoke();
        if (!_updatingUi)
            VolumeChanged?.Invoke(e.NewValue);
    }

    private void SpeedComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UserActivity?.Invoke();
        if (!_updatingUi &&
            SpeedComboBox.SelectedIndex >= 0 &&
            SpeedComboBox.SelectedIndex < Speeds.Length)
        {
            SpeedChanged?.Invoke(Speeds[SpeedComboBox.SelectedIndex]);
        }
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        UserActivity?.Invoke();
        TogglePauseRequested?.Invoke();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        UserActivity?.Invoke();
        BackRequested?.Invoke();
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        UserActivity?.Invoke();
        ForwardRequested?.Invoke();
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        UserActivity?.Invoke();
        MuteRequested?.Invoke();
    }

    private void Audio_Click(object sender, RoutedEventArgs e)
    {
        UserActivity?.Invoke();
        AudioRequested?.Invoke();
    }

    private void Subtitle_Click(object sender, RoutedEventArgs e)
    {
        UserActivity?.Invoke();
        SubtitleRequested?.Invoke();
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        UserActivity?.Invoke();
        FullscreenRequested?.Invoke();
    }

    private static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
            seconds = 0;

        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }

    private static IntPtr GetWindowLongPtr(IntPtr hwnd, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : new IntPtr(GetWindowLong32(hwnd, index));

    private static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, index, value) : new IntPtr(SetWindowLong32(hwnd, index, value.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
}
