using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Midview.Protocol;

namespace Midview.Master;

public partial class ViewControlWindow : Window
{
    private readonly ConnectionClient _client;
    private readonly DispatcherTimer _heartbeatTimer;
    private WriteableBitmap? _screenBitmap;
    private bool _closing;

    public ViewControlWindow(ConnectionClient client)
    {
        InitializeComponent();
        _client = client;

        _client.SendStartControl();

        _heartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _heartbeatTimer.Tick += (_, _) => RunOrCloseOnFailure(() => _client.SendHeartbeat());
        _heartbeatTimer.Start();

        Task.Run(ReceiveLoop);
    }

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
        var position = e.GetPosition(ScreenImage);
        SendInput(InputEventKind.MouseMove, (int)position.X, (int)position.Y, 0);
    }

    private void OnMouseLeftDown(object sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(ScreenImage);
        SendInput(InputEventKind.MouseLeftDown, (int)position.X, (int)position.Y, 0);
    }

    private void OnMouseLeftUp(object sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(ScreenImage);
        SendInput(InputEventKind.MouseLeftUp, (int)position.X, (int)position.Y, 0);
    }

    private void OnKeyDown(object sender, KeyEventArgs e) =>
        SendInput(InputEventKind.KeyDown, 0, 0, (ushort)KeyInterop.VirtualKeyFromKey(e.Key));

    private void OnKeyUp(object sender, KeyEventArgs e) =>
        SendInput(InputEventKind.KeyUp, 0, 0, (ushort)KeyInterop.VirtualKeyFromKey(e.Key));

    private void SendInput(InputEventKind kind, int x, int y, ushort virtualKeyCode)
    {
        var buffer = new byte[11];
        buffer[0] = (byte)kind;
        BitConverter.GetBytes(x).CopyTo(buffer, 1);
        BitConverter.GetBytes(y).CopyTo(buffer, 5);
        BitConverter.GetBytes(virtualKeyCode).CopyTo(buffer, 9);
        RunOrCloseOnFailure(() => _client.SendInputEvent(buffer));
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

    private enum InputEventKind : byte { MouseMove = 0, MouseLeftDown = 1, MouseLeftUp = 2, KeyDown = 3, KeyUp = 4 }
}
