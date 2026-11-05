using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

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
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
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
        ShowTitleLogo();
    }

    // The title bar's logo (again on a scale change: MainWindow.TileLayout.cs's OnDpiChanged): the picture in the program's icon that is just
    // big enough for this screen's scale, so it is never scaled up into a
    // blur, and small enough to be the icon's simpler small-size drawing.
    private void ShowTitleLogo()
    {
        try
        {
            var decoder = BitmapDecoder.Create(
                new Uri("pack://application:,,,/Midview.Master;component/midview.ico"),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var size = LogoFrameSize(decoder.Frames.Select(frame => frame.PixelWidth).ToList(),
                TitleLogo.Width * VisualTreeHelper.GetDpi(this).DpiScaleX);
            TitleLogo.Source = decoder.Frames.First(frame => frame.PixelWidth == size);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException)
        {
            // Without its picture the title bar still reads "Midview".
        }
    }

    // The smallest picture at least as big as the logo on screen, or the
    // biggest when none is.
    internal static int LogoFrameSize(IReadOnlyCollection<int> sizes, double neededPixels) =>
        sizes.Where(size => size >= neededPixels).DefaultIfEmpty(sizes.Max()).Min();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        ShowWindowState();
    }

    // The caption below the 6 px top resize band, down to the bar's bottom
    // (32): not over the toolbar under it.
    private const double RestoredCaptionHeight = 26;

    private void ShowWindowState()
    {
        var maximized = WindowState == WindowState.Maximized;
        WindowRoot.Margin = maximized ? MaximizedOverhang() : new Thickness(0);
        // Maximized, the bar moves down by the margin, past the resize band
        // (which is off the screen then): the caption follows it.
        if (WindowChrome.GetWindowChrome(this) is { } chrome)
        {
            chrome.CaptionHeight = RestoredCaptionHeight + (maximized ? WindowRoot.Margin.Top : 0);
        }
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
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
            case WmNcHitTest:
                var hit = TopEdgeHit(lParam);
                if (hit == 0 && IsOverMaximizeButton(lParam))
                {
                    hit = HtMaxButton;
                }
                if (hit != 0)
                {
                    handled = true;
                    return new IntPtr(hit);
                }
                break;
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

    // Restored, the top resize band still resizes over the bar's buttons,
    // which WindowChrome would give to the buttons themselves (they take
    // their clicks in its caption) and this hook to the snap layouts. 0
    // anywhere else, left to WindowChrome and to the maximize button.
    private int TopEdgeHit(IntPtr lParam)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (WindowState != WindowState.Normal || WindowChrome.GetWindowChrome(this) is not { } chrome
            || hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var window))
        {
            return 0;
        }
        var (x, y) = ScreenPoint(lParam);
        var dpi = VisualTreeHelper.GetDpi(this);
        var band = chrome.ResizeBorderThickness;
        if (y < window.Top || y >= window.Top + band.Top * dpi.DpiScaleY)
        {
            return 0;
        }
        if (x >= window.Right - band.Right * dpi.DpiScaleX)
        {
            return HtTopRight;
        }
        return x < window.Left + band.Left * dpi.DpiScaleX ? HtTopLeft : HtTop;
    }

    // lParam holds the mouse's screen position in pixels, as two signed
    // 16-bit numbers (negative on a monitor left of or above the main one).
    private static (int X, int Y) ScreenPoint(IntPtr lParam) =>
        ((short)(lParam.ToInt64() & 0xFFFF), (short)((lParam.ToInt64() >> 16) & 0xFFFF));

    private bool IsOverMaximizeButton(IntPtr lParam)
    {
        if (!MaximizeButton.IsVisible)
        {
            return false;
        }
        var (x, y) = ScreenPoint(lParam);
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
