using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Midview.Protocol;

namespace Midview.Master;

// Watching only leaves the student's keyboard and mouse alone; controlling
// locks them and sends the teacher's input instead. The toolbar button
// switches between the two on the same connection.
public partial class ViewControlWindow : Window
{
    private readonly ConnectionClient _client;
    private readonly string _machineName;
    private readonly DispatcherTimer _heartbeatTimer;
    private WriteableBitmap? _screenBitmap;
    private bool _closing;
    private bool _controlling;

    public ViewControlWindow(ConnectionClient client, string machineName, bool control)
    {
        InitializeComponent();
        _client = client;
        _machineName = machineName;

        // The first message starts the screen stream either way; a heartbeat
        // starts it without locking anything.
        if (control)
        {
            _client.SendStartControl();
        }
        else
        {
            _client.SendHeartbeat();
        }
        _controlling = control;
        ShowMode();

        _heartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _heartbeatTimer.Tick += (_, _) => RunOrCloseOnFailure(() => _client.SendHeartbeat());
        _heartbeatTimer.Start();

        Task.Run(ReceiveLoop);
    }

    // ScreenImage doesn't receive keyboard focus just by being Focusable and
    // visible -- without this, KeyDown/KeyUp never fire at all.
    private void OnWindowLoaded(object sender, RoutedEventArgs e) => ScreenImage.Focus();

    // Every send/receive here crosses the network; the connection can die at
    // any point (the remote side closing, a transient failure, ...). Instead
    // of letting that exception escape into WPF's dispatcher loop -- which
    // has no handler and crashes the whole Master app -- this closes just
    // this window and leaves the rest of the app running.
    private void RunOrCloseOnFailure(Action action)
    {
        try
        {
            action();
        }
        catch (IOException)
        {
            CloseFromFailure();
        }
    }

    private void CloseFromFailure() => Dispatcher.Invoke(() =>
    {
        if (!_closing)
        {
            Close();
        }
    });

    private void ReceiveLoop()
    {
        while (true)
        {
            Message message;
            try
            {
                message = _client.ReceiveNext();
            }
            catch (IOException)
            {
                CloseFromFailure();
                return; // connection closed
            }

            if (message.Type != MessageType.FrameTile)
            {
                continue;
            }

            var row = BitConverter.ToInt32(message.Payload, 0);
            var column = BitConverter.ToInt32(message.Payload, 4);
            var jpegBytes = message.Payload[8..];

            try
            {
                Dispatcher.Invoke(() => DrawTile(row, column, jpegBytes));
            }
            catch (Exception)
            {
                // A single malformed/out-of-bounds tile shouldn't take down
                // the whole view -- just skip it and keep receiving.
            }
        }
    }

    private int _bitmapWidth;
    private int _bitmapHeight;

    private void DrawTile(int row, int column, byte[] jpegBytes)
    {
        const int tileSize = 64; // must match Midview.Protocol.TileDiff.TileSize

        using var stream = new MemoryStream(jpegBytes);
        var decoder = new JpegBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var tileFrame = decoder.Frames[0];

        // The Session's actual screen resolution isn't known ahead of time
        // (it isn't sent over the wire), so grow the canvas to fit whatever
        // tile positions actually arrive instead of assuming a fixed size --
        // a hardcoded guess left the view permanently blank on any machine
        // with a different resolution.
        var requiredWidth = column * tileSize + tileFrame.PixelWidth;
        var requiredHeight = row * tileSize + tileFrame.PixelHeight;
        if (_screenBitmap is null || requiredWidth > _bitmapWidth || requiredHeight > _bitmapHeight)
        {
            GrowCanvas(Math.Max(_bitmapWidth, requiredWidth), Math.Max(_bitmapHeight, requiredHeight));
        }

        var rect = new Int32Rect(column * tileSize, row * tileSize, tileFrame.PixelWidth, tileFrame.PixelHeight);
        var stride = tileFrame.PixelWidth * 4;
        var pixels = new byte[stride * tileFrame.PixelHeight];
        tileFrame.CopyPixels(pixels, stride, 0);
        _screenBitmap!.WritePixels(rect, pixels, stride, 0);

        ScreenImage.Source = _screenBitmap;
    }

    private void GrowCanvas(int newWidth, int newHeight)
    {
        var newBitmap = new WriteableBitmap(newWidth, newHeight, 96, 96, PixelFormats.Bgra32, null);
        if (_screenBitmap is not null)
        {
            var oldRect = new Int32Rect(0, 0, _bitmapWidth, _bitmapHeight);
            var oldStride = _bitmapWidth * 4;
            var oldPixels = new byte[oldStride * _bitmapHeight];
            _screenBitmap.CopyPixels(oldRect, oldPixels, oldStride, 0);
            newBitmap.WritePixels(oldRect, oldPixels, oldStride, 0);
        }

        _screenBitmap = newBitmap;
        _bitmapWidth = newWidth;
        _bitmapHeight = newHeight;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var position = ToBitmapCoordinates(e.GetPosition(ScreenImage));
        SendInput(InputEventKind.MouseMove, (int)position.X, (int)position.Y, 0);
    }

    private void OnMouseLeftDown(object sender, MouseButtonEventArgs e)
    {
        ScreenImage.Focus(); // clicking into the view should always (re)grab keyboard focus
        var position = ToBitmapCoordinates(e.GetPosition(ScreenImage));
        SendInput(InputEventKind.MouseLeftDown, (int)position.X, (int)position.Y, 0);
    }

    private void OnMouseLeftUp(object sender, MouseButtonEventArgs e)
    {
        var position = ToBitmapCoordinates(e.GetPosition(ScreenImage));
        SendInput(InputEventKind.MouseLeftUp, (int)position.X, (int)position.Y, 0);
    }

    private void OnMouseRightDown(object sender, MouseButtonEventArgs e)
    {
        ScreenImage.Focus();
        var position = ToBitmapCoordinates(e.GetPosition(ScreenImage));
        SendInput(InputEventKind.MouseRightDown, (int)position.X, (int)position.Y, 0);
    }

    private void OnMouseRightUp(object sender, MouseButtonEventArgs e)
    {
        var position = ToBitmapCoordinates(e.GetPosition(ScreenImage));
        SendInput(InputEventKind.MouseRightUp, (int)position.X, (int)position.Y, 0);
    }

    // ScreenImage now stretches "Uniform" to always show the whole remote
    // screen regardless of window size, instead of clipping it at native
    // pixel size -- so a click's position within the control no longer maps
    // 1:1 to a remote pixel. This converts back to real bitmap coordinates,
    // accounting for both the scale factor and any letterboxing offset when
    // the window's aspect ratio doesn't match the remote screen's.
    private Point ToBitmapCoordinates(Point controlPosition)
    {
        if (_bitmapWidth <= 0 || _bitmapHeight <= 0 || ScreenImage.ActualWidth <= 0 || ScreenImage.ActualHeight <= 0)
        {
            return controlPosition;
        }

        var controlAspect = ScreenImage.ActualWidth / ScreenImage.ActualHeight;
        var bitmapAspect = (double)_bitmapWidth / _bitmapHeight;

        double renderedWidth, renderedHeight, offsetX, offsetY;
        if (controlAspect > bitmapAspect)
        {
            renderedHeight = ScreenImage.ActualHeight;
            renderedWidth = renderedHeight * bitmapAspect;
            offsetX = (ScreenImage.ActualWidth - renderedWidth) / 2;
            offsetY = 0;
        }
        else
        {
            renderedWidth = ScreenImage.ActualWidth;
            renderedHeight = renderedWidth / bitmapAspect;
            offsetX = 0;
            offsetY = (ScreenImage.ActualHeight - renderedHeight) / 2;
        }

        var x = (controlPosition.X - offsetX) * (_bitmapWidth / renderedWidth);
        var y = (controlPosition.Y - offsetY) * (_bitmapHeight / renderedHeight);
        return new Point(x, y);
    }

    private void OnKeyDown(object sender, KeyEventArgs e) => SendKey(InputEventKind.KeyDown, e);

    private void OnKeyUp(object sender, KeyEventArgs e) => SendKey(InputEventKind.KeyUp, e);

    private void SendKey(InputEventKind kind, KeyEventArgs e)
    {
        if (!_controlling)
        {
            return;
        }

        // WPF reports Alt, F10 and every Alt+key combination as Key.System,
        // with the real key in SystemKey -- e.Key alone would send a
        // meaningless code for all of them.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        SendInput(kind, 0, 0, (ushort)KeyInterop.VirtualKeyFromKey(key));

        // Keeps this window from acting on the key itself: Tab and arrows
        // would move focus away from the view (silently dropping every key
        // after that), and Alt/F10 would open the window's system menu.
        e.Handled = true;
    }

    private void SendInput(InputEventKind kind, int x, int y, ushort virtualKeyCode)
    {
        if (!_controlling)
        {
            return;
        }

        var buffer = new byte[11];
        buffer[0] = (byte)kind;
        BitConverter.GetBytes(x).CopyTo(buffer, 1);
        BitConverter.GetBytes(y).CopyTo(buffer, 5);
        BitConverter.GetBytes(virtualKeyCode).CopyTo(buffer, 9);
        RunOrCloseOnFailure(() => _client.SendInputEvent(buffer));
    }

    private void OnControlClicked(object sender, RoutedEventArgs e)
    {
        RunOrCloseOnFailure(() =>
        {
            if (_controlling)
            {
                _client.SendStopControl();
            }
            else
            {
                _client.SendStartControl();
            }
            _controlling = !_controlling;
        });
        ShowMode();
        ScreenImage.Focus();
    }

    private void ShowMode()
    {
        Title = $"Midview - {(_controlling ? "controlando" : "vendo")} {_machineName}";
        ControlButton.Content = _controlling ? "Parar de controlar (só ver)" : "Controlar";
        ModeTextBlock.Text = _controlling
            ? "Controlando: o teclado e o mouse do aluno estão travados."
            : "Só vendo: o aluno continua usando o computador normalmente.";
    }

    private void OnDisconnectClicked(object sender, RoutedEventArgs e) => Close();

    private void OnFullscreenClicked(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            FullscreenButton.Content = "Tela Cheia";
        }
        else
        {
            WindowState = WindowState.Maximized;
            FullscreenButton.Content = "Restaurar";
        }
    }

    private void OnWindowClosed(object sender, EventArgs e)
    {
        _closing = true;
        _heartbeatTimer.Stop();
        try
        {
            _client.SendStopControl();
        }
        catch (IOException)
        {
            // Connection may already be dead; closing should never throw.
        }
        _client.Dispose();
    }

    // Must stay numerically aligned with Midview.Session.InputEventKind --
    // the wire format is a raw byte, not the enum name.
    private enum InputEventKind : byte
    {
        MouseMove = 0, MouseLeftDown = 1, MouseLeftUp = 2, KeyDown = 3, KeyUp = 4,
        MouseRightDown = 5, MouseRightUp = 6,
    }
}
