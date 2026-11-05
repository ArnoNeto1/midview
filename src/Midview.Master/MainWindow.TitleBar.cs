using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;

namespace Midview.Master;

// The classroom window's own title bar (MainWindow.xaml's WindowChrome): the
// Midview mark, the lesson on and how many machines answer, and the
// minimize, maximize and close buttons. Windows still drags the window by
// the bar, maximizes it on a double click there and snaps it to the screen's
// edges. The maximize button answers Windows' hit test as its own maximize
// button (HTMAXBUTTON), so on Windows 11 resting the mouse on it shows the
// snap layouts; Windows then sends the mouse over it as non-client messages,
// so its hover and its click are handled here rather than by WPF.
public partial class MainWindow
{
    private const int WmMouseMove = 0x0200;
    private const int WmNcHitTest = 0x0084;
    private const int WmNcMouseMove = 0x00A0;
    private const int WmNcLButtonDown = 0x00A1;
    private const int WmNcLButtonUp = 0x00A2;
    private const int WmNcLButtonDblClk = 0x00A3;
    private const int WmNcMouseLeave = 0x02A2;
    private const int HtMaxButton = 9;
    private const uint TmeLeave = 0x02;
    private const uint TmeNonClient = 0x10;

    // What the maximize button shows, as WPF can't see the mouse over it.
    private bool _maximizeHovered;
    private bool _maximizePressed;

    // The lesson on (ClassFolderButton.Caption, from ShowClassLesson) and how
    // many of the room's machines answer, after every change to either.
    private void UpdateTitleChips()
    {
        var turma = ClassFolderButton.Caption;
        LessonChipText.Text = $"Aula: {turma}";
        LessonChip.Visibility = string.IsNullOrEmpty(turma) ? Visibility.Collapsed : Visibility.Visible;

        var online = _tiles.Count(tile => tile.Presence == TilePresence.Online);
        var count = MachineCountText.Online(online, _tiles.Count);
        MachineCountChipText.Text = count;
        MachineCountChip.Visibility = count.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(OnTitleBarMessage);
        ShowWindowState();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        ShowWindowState();
    }

    private void ShowWindowState()
    {
        var maximized = WindowState == WindowState.Maximized;
        WindowRoot.Margin = maximized ? MaximizedOverhang() : new Thickness(0);
        MaximizeButton.Content = maximized ? "" : "";
        AutomationProperties.SetName(MaximizeButton, maximized ? "Restaurar" : "Maximizar");
    }

    // Maximized, Windows lays the window's edges past the screen's work area
    // by its resize frame, which is SystemParameters.WindowResizeBorderThickness
    // plus a padded border that WPF doesn't report (at 125%: 3.2 + 4 units).
    // The content steps in by what the window really overhangs, so nothing is
    // cut off; when that can't be read, by the resize border plus 2.
    private Thickness MaximizedOverhang()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var window)
            || !GetMonitorInfo(MonitorFromWindow(hwnd, MonitorDefaultToNearest), ref monitor))
        {
            var border = SystemParameters.WindowResizeBorderThickness;
            return new Thickness(border.Left + 2, border.Top + 2, border.Right + 2, border.Bottom + 2);
        }
        var dpi = VisualTreeHelper.GetDpi(this);
        var work = monitor.Work;
        return new Thickness(
            Math.Max(0, work.Left - window.Left) / dpi.DpiScaleX,
            Math.Max(0, work.Top - window.Top) / dpi.DpiScaleY,
            Math.Max(0, window.Right - work.Right) / dpi.DpiScaleX,
            Math.Max(0, window.Bottom - work.Bottom) / dpi.DpiScaleY);
    }

    private void OnMinimizeClicked(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    // Only reached through UI Automation: the mouse's click comes as WM_NCLBUTTONUP.
    private void OnMaximizeClicked(object sender, RoutedEventArgs e) => ToggleMaximized();

    private void OnCloseClicked(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void ToggleMaximized()
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private IntPtr OnTitleBarMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (message)
        {
            case WmNcHitTest when IsOverMaximizeButton(lParam):
                handled = true;
                return new IntPtr(HtMaxButton);
            case WmNcMouseMove:
                // Not handled: Windows goes on to show the snap layouts.
                var over = wParam.ToInt32() == HtMaxButton;
                if (over && !_maximizeHovered)
                {
                    TrackNonClientLeave(hwnd);
                }
                ShowMaximizeButton(hovered: over, pressed: over && _maximizePressed);
                break;
            case WmNcMouseLeave:
                ShowMaximizeButton(hovered: false, pressed: false);
                break;
            case WmMouseMove when _maximizeHovered:
                ShowMaximizeButton(hovered: false, pressed: false);
                break;
            // Kept from Windows, which would otherwise track the press itself.
            case WmNcLButtonDown or WmNcLButtonDblClk when wParam.ToInt32() == HtMaxButton:
                ShowMaximizeButton(hovered: true, pressed: true);
                handled = true;
                break;
            case WmNcLButtonUp when wParam.ToInt32() == HtMaxButton:
                var click = _maximizePressed;
                ShowMaximizeButton(hovered: true, pressed: false);
                handled = true;
                if (click)
                {
                    ToggleMaximized();
                }
                break;
        }
        return IntPtr.Zero;
    }

    // lParam holds the mouse's screen position in pixels, as two signed
    // 16-bit numbers (negative on a monitor left of or above the main one).
    private bool IsOverMaximizeButton(IntPtr lParam)
    {
        if (!MaximizeButton.IsVisible)
        {
            return false;
        }
        var x = (short)(lParam.ToInt64() & 0xFFFF);
        var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        var topLeft = MaximizeButton.PointToScreen(new Point(0, 0));
        var bottomRight = MaximizeButton.PointToScreen(new Point(MaximizeButton.ActualWidth, MaximizeButton.ActualHeight));
        return x >= topLeft.X && x < bottomRight.X && y >= topLeft.Y && y < bottomRight.Y;
    }

    private void ShowMaximizeButton(bool hovered, bool pressed)
    {
        _maximizeHovered = hovered;
        _maximizePressed = pressed;
        if (pressed)
        {
            MaximizeButton.SetResourceReference(BackgroundProperty, "ToolbarPressedBrush");
        }
        else if (hovered)
        {
            MaximizeButton.SetResourceReference(BackgroundProperty, "ToolbarHoverBrush");
        }
        else
        {
            MaximizeButton.ClearValue(BackgroundProperty);
        }
    }

    // Windows says when the mouse leaves the maximize button for the window's
    // inside or elsewhere only when asked, once per entry.
    private static void TrackNonClientLeave(IntPtr hwnd)
    {
        var track = new TrackMouseEventInfo
        {
            Size = (uint)Marshal.SizeOf<TrackMouseEventInfo>(),
            Flags = TmeLeave | TmeNonClient,
            Window = hwnd,
        };
        TrackMouseEvent(ref track);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouseEventInfo
    {
        public uint Size;
        public uint Flags;
        public IntPtr Window;
        public uint HoverTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect32 Monitor;
        public Rect32 Work;
        public uint Flags;
    }

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern bool TrackMouseEvent(ref TrackMouseEventInfo track);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
