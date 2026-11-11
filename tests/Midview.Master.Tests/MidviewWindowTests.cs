using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Midview.Master;
using Xunit;
using D = System.Drawing;

// The windows here get a handle but are never shown: nothing reaches the screen.
public class MidviewWindowTests
{
    private const int EraseBackground = 0x0014; // WM_ERASEBKGND
    private const int White = 0xFFFFFF;

    // As Windows shows a window it asks it to erase its background, before WPF
    // has drawn anything. WPF answers "done" and paints nothing, which left the
    // window white for a frame or two as it opened.
    [Fact]
    public void BeforeTheFirstDrawing_AnErasePaintsTheWindowsOwnColor()
    {
        OnAWindow(window => Assert.Equal(0x123456, ColorAfterAnErase(window)));
    }

    // Once WPF has drawn the window, what is on it is WPF's drawing: an erase
    // that painted over it would blink.
    [Fact]
    public void AfterTheFirstDrawing_AnEraseLeavesTheWindowAlone()
    {
        OnAWindow(window =>
        {
            window.Drawn();
            Assert.Equal(White, ColorAfterAnErase(window));
        });
    }

    private sealed class Probe : MidviewWindow
    {
        public void Drawn() => OnContentRendered(EventArgs.Empty);
    }

    // What a white sheet looks like (0xRRGGBB) after the window was asked to
    // erase its background onto it.
    private static int ColorAfterAnErase(Probe window)
    {
        using var sheet = new D.Bitmap(8, 8, D.Imaging.PixelFormat.Format24bppRgb);
        using (var g = D.Graphics.FromImage(sheet))
        {
            g.Clear(D.Color.White);
            var hdc = g.GetHdc();
            try
            {
                SendMessage(new WindowInteropHelper(window).Handle, EraseBackground, hdc, IntPtr.Zero);
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }
        return sheet.GetPixel(4, 4).ToArgb() & 0xFFFFFF;
    }

    // A window needs a thread of its own kind (STA).
    private static void OnAWindow(Action<Probe> check)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new Probe
                {
                    Width = 200, Height = 150, Background = new SolidColorBrush(Color.FromRgb(0x12, 0x34, 0x56)),
                };
                new WindowInteropHelper(window).EnsureHandle();
                try
                {
                    check(window);
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception e)
            {
                failure = e;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
}
