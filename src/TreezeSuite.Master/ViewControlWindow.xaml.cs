using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TreezeSuite.Protocol;

namespace TreezeSuite.Master;

public partial class ViewControlWindow : Window
{
    private readonly ConnectionClient _client;
    private readonly DispatcherTimer _heartbeatTimer;
    private WriteableBitmap? _screenBitmap;

    public ViewControlWindow(ConnectionClient client)
    {
        InitializeComponent();
        _client = client;

        _client.SendStartControl();

        _heartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _heartbeatTimer.Tick += (_, _) => _client.SendHeartbeat();
        _heartbeatTimer.Start();

        Task.Run(ReceiveLoop);
    }

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
                return; // connection closed
            }

            if (message.Type != MessageType.FrameTile)
            {
                continue;
            }

            var row = BitConverter.ToInt32(message.Payload, 0);
            var column = BitConverter.ToInt32(message.Payload, 4);
            var jpegBytes = message.Payload[8..];

            Dispatcher.Invoke(() => DrawTile(row, column, jpegBytes));
        }
    }

    private void DrawTile(int row, int column, byte[] jpegBytes)
    {
        const int tileSize = 64; // must match TreezeSuite.Protocol.TileDiff.TileSize

        using var stream = new MemoryStream(jpegBytes);
        var decoder = new JpegBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var tileFrame = decoder.Frames[0];

        _screenBitmap ??= new WriteableBitmap(1920, 1080, 96, 96, PixelFormats.Bgra32, null);

        var rect = new Int32Rect(column * tileSize, row * tileSize, tileFrame.PixelWidth, tileFrame.PixelHeight);
        var stride = tileFrame.PixelWidth * 4;
        var pixels = new byte[stride * tileFrame.PixelHeight];
        tileFrame.CopyPixels(pixels, stride, 0);
        _screenBitmap.WritePixels(rect, pixels, stride, 0);

        ScreenImage.Source = _screenBitmap;
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
        _client.SendInputEvent(buffer);
    }

    private void OnWindowClosed(object sender, EventArgs e)
    {
        _heartbeatTimer.Stop();
        _client.SendStopControl();
        _client.Dispose();
    }

    private enum InputEventKind : byte { MouseMove = 0, MouseLeftDown = 1, MouseLeftUp = 2, KeyDown = 3, KeyUp = 4 }
}
