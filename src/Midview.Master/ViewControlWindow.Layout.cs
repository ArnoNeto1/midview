using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Midview.Master;

// How the student's screen takes the room: the toolbar floats over the
// picture and slides away (ViewToolbarAutoHide), the window opens in the
// shape of the student's screen (ViewWindowFit), and "Tela Cheia" hides the
// title bar and the teacher's taskbar too.
public partial class ViewControlWindow
{
    private static readonly Duration SlideTime = new(TimeSpan.FromMilliseconds(150));

    // The mouse is on the top edge within this many units of it.
    private const double TopEdge = 1;

    private readonly ViewToolbarAutoHide _toolbarAutoHide = new();
    private DispatcherTimer? _toolbarTimer;
    private bool _toolbarShownOnScreen = true;
    private bool _fullScreen;
    private WindowState _stateBeforeFullScreen;
    private bool _fitPending;

    private void OnViewMouseMove(object sender, MouseEventArgs e)
    {
        var atTopEdge = e.GetPosition(ViewRoot).Y <= TopEdge;
        _toolbarAutoHide.MouseMoved(DateTime.UtcNow, atTopEdge, Toolbar.IsMouseOver);
        SyncToolbar();
    }

    // As the mouse heads for another window, the buttons are there when it
    // comes back; a window in the background keeps its picture clear.
    private void OnViewMouseLeave(object sender, MouseEventArgs e)
    {
        if (!IsActive)
        {
            return;
        }
        _toolbarAutoHide.MouseLeftWindow();
        SyncToolbar();
    }

    // Called each time the picture is made at a new size.
    private void OnPictureSized()
    {
        _toolbarAutoHide.PictureArrived(DateTime.UtcNow, Toolbar.IsMouseOver);
        SyncToolbar();
        FitWindowToPicture();
    }

    private void SyncToolbar()
    {
        var timer = _toolbarTimer ??= NewToolbarTimer();
        if (_toolbarAutoHide.Waiting)
        {
            timer.Start();
        }
        else
        {
            timer.Stop();
        }

        var shown = _toolbarAutoHide.Shown;
        if (shown == _toolbarShownOnScreen)
        {
            return;
        }
        _toolbarShownOnScreen = shown;
        ToolbarSlide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(shown ? 0 : -Toolbar.ActualHeight, SlideTime));
    }

    private DispatcherTimer NewToolbarTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            _toolbarAutoHide.Update(DateTime.UtcNow);
            SyncToolbar();
        };
        return timer;
    }

    // Only a window in its normal state is resized: a maximized or full
    // screen one keeps the size the teacher chose.
    private void FitWindowToPicture()
    {
        if (!IsLoaded)
        {
            _fitPending = true;
            return;
        }
        _fitPending = false;
        if (_fullScreen || WindowState != WindowState.Normal || Content is not FrameworkElement content
            || content.ActualWidth <= 0 || content.ActualHeight <= 0)
        {
            return;
        }

        // The title bar and the borders: what the window has beyond its content.
        var frameWidth = ActualWidth - content.ActualWidth;
        var frameHeight = ActualHeight - content.ActualHeight;
        var work = ViewWindowFit.WorkArea(this);
        var dpi = VisualTreeHelper.GetDpi(this);
        var client = ViewWindowFit.ClientSize(_bitmapWidth, _bitmapHeight, dpi.DpiScaleX, dpi.DpiScaleY,
            new Size(work.Width - frameWidth, work.Height - frameHeight));
        if (client.IsEmpty)
        {
            return;
        }

        Width = client.Width + frameWidth;
        Height = client.Height + frameHeight;
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + (work.Height - Height) / 2;
    }

    private void OnFullscreenClicked(object sender, RoutedEventArgs e)
    {
        SetFullScreen(!_fullScreen);
        ScreenImage.Focus();
    }

    private void SetFullScreen(bool on)
    {
        if (on == _fullScreen)
        {
            return;
        }
        _fullScreen = on;
        if (on)
        {
            _stateBeforeFullScreen = WindowState;
            // A maximized window keeps its size when the border goes away:
            // restored first, it covers the whole screen, taskbar included,
            // once maximized again without the border.
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _stateBeforeFullScreen == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        }
        FullscreenButton.Content = on ? "Sair da tela cheia" : "Tela Cheia";
    }

    // Esc leaves full screen while only watching; while controlling it is
    // the student's key.
    private bool LeftFullScreenByKey(KeyEventArgs e)
    {
        if (_controlling || !_fullScreen || e.Key != Key.Escape)
        {
            return false;
        }
        SetFullScreen(false);
        e.Handled = true;
        return true;
    }
}
