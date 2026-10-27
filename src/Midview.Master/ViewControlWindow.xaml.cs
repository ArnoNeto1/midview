using System.IO;
using System.Security.Cryptography.X509Certificates;
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
    private readonly RemoteScreen _remoteScreen = new();
    private WriteableBitmap? _screenBitmap;
    private bool _closing;
    private bool _controlling;
    private readonly ControlEndpoint _endpoint;
    private readonly KnownMachine? _machine;
    private readonly X509Certificate2? _masterCertificate;
    private ControlLockKeeper? _lockKeeper;

    // 1 while a DrawChanges is waiting to run on the window's thread.
    private int _drawScheduled;

    // endpoint: what client is connected to. Through the helper (Desk) the
    // student's input can't be locked by it, so while controlling this window
    // keeps a ControlLockKeeper on the Session -- which needs the machine and
    // the certificate; a Session connection locks the input itself.
    public ViewControlWindow(
        ConnectionClient client, string machineName, bool control,
        ControlEndpoint endpoint = ControlEndpoint.Session,
        KnownMachine? machine = null, X509Certificate2? masterCertificate = null)
    {
        InitializeComponent();
        _client = client;
        _machineName = machineName;
        _endpoint = endpoint;
        _machine = machine;
        _masterCertificate = masterCertificate;

        // Asks for the screen as ScreenFrame updates, which starts it; a
        // Session from before that ignores the type and sends FrameTile,
        // which is drawn just the same. StartControl then locks the student;
        // a heartbeat only watches.
        _client.SendScreenStream();
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
        _heartbeatTimer.Tick += (_, _) => RunOrCloseOnFailure(() =>
        {
            _client.SendHeartbeat();
            PushClipboardIfChanged();
        });
        _heartbeatTimer.Start();

        Task.Run(ReceiveLoop);

        // Last, so a constructor that fails above leaves no connection behind.
        SyncLockKeeper();
    }

    // The input lock is held only while controlling through the helper: a
    // reachable helper means the machine runs the current build, whose
    // Session knows HoldInputLock (the Session path locks by itself).
    private void SyncLockKeeper()
    {
        // The only place a keeper is made, and none may be made once the
        // window is closing: OnWindowClosed has already disposed it, nothing
        // would dispose a new one, and the student would stay locked. (A send
        // that fails inside OnControlClicked closes the window in the middle
        // of the handler, which then still reaches here.)
        if (_closing)
        {
            _lockKeeper?.Dispose();
            _lockKeeper = null;
            return;
        }

        if (_controlling && _endpoint == ControlEndpoint.Helper && _machine is not null && _masterCertificate is not null)
        {
            if (_lockKeeper is null)
            {
                _lockKeeper = new ControlLockKeeper(_machine, _masterCertificate);
                _lockKeeper.Start();
            }
        }
        else
        {
            _lockKeeper?.Dispose();
            _lockKeeper = null;
        }
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
            catch (Exception)
            {
                // Connection closed, or a message this can't read (longer
                // than MessageFramer allows, say): past that the stream can't
                // be trusted, and a window that closes says more than a
                // picture that silently stops changing.
                CloseFromFailure();
                return;
            }

            // Decoded here, off the window's thread. A single malformed or
            // out-of-bounds update shouldn't take down the whole view: it's
            // skipped and receiving goes on.
            try
            {
                switch (message.Type)
                {
                    case MessageType.ScreenFrame:
                        if (ScreenFrame.Decode(message.Payload) is not { } frame)
                        {
                            continue;
                        }
                        _remoteScreen.AddFrame(frame);
                        break;

                    case MessageType.FrameTile when message.Payload.Length >= 8:
                        var row = BitConverter.ToInt32(message.Payload, 0);
                        var column = BitConverter.ToInt32(message.Payload, 4);
                        _remoteScreen.AddTile(row, column, message.Payload[8..]);
                        break;

                    case MessageType.ClipboardText:
                        if (ClipboardText.Decode(message.Payload) is { } incomingClip)
                        {
                            Dispatcher.BeginInvoke(new Action(() => ApplyClipboard(incomingClip)));
                        }
                        continue;

                    default:
                        continue;
                }
            }
            catch (Exception)
            {
                continue;
            }

            // One draw for however many updates arrive before it runs.
            if (Interlocked.Exchange(ref _drawScheduled, 1) == 0)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Render, DrawChanges);
            }
        }
    }

    private int _bitmapWidth;
    private int _bitmapHeight;

    // On the window's thread: copies what changed into the picture shown,
    // then tells the Session the newest update is on screen.
    private void DrawChanges()
    {
        Volatile.Write(ref _drawScheduled, 0); // anything arriving from now on schedules another draw
        if (_closing)
        {
            return;
        }

        var frameId = _remoteScreen.TakeChanges(picture =>
        {
            try
            {
                // The Session's resolution arrives with the picture (or, from
                // a Session from before ScreenFrame, the picture grows to fit
                // its tiles), so the bitmap is made again only when it changes.
                var parts = picture.Changed;
                if (picture.Resized || _screenBitmap is null)
                {
                    _screenBitmap = new WriteableBitmap(picture.Width, picture.Height, 96, 96, PixelFormats.Bgr32, null);
                    _bitmapWidth = picture.Width;
                    _bitmapHeight = picture.Height;
                    ScreenImage.Source = _screenBitmap;
                    parts = new[] { new PixelRect(0, 0, picture.Width, picture.Height) };
                }

                foreach (var part in parts)
                {
                    _screenBitmap.WritePixels(
                        new Int32Rect(part.X, part.Y, part.Width, part.Height), picture.Pixels, picture.Stride, part.X, part.Y);
                }
            }
            catch (Exception)
            {
                // As in ReceiveLoop: a bad update is skipped, the view stays.
            }
        });

        if (frameId != 0)
        {
            RunOrCloseOnFailure(() => _client.SendFrameAck(frameId));
        }
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

    // The text last synced either way, so the same value isn't sent back as
    // soon as it is applied (which would loop with the student's side).
    private string? _lastClipboard;

    // While controlling, sends the teacher's copied text to the student so it
    // can be pasted there. Called on the heartbeat tick (the window's thread,
    // which is where WPF's clipboard must be read).
    private void PushClipboardIfChanged()
    {
        if (!_controlling)
        {
            return;
        }
        string text;
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        }
        catch (Exception)
        {
            return; // clipboard busy; try again next tick
        }
        if (string.IsNullOrEmpty(text) || text == _lastClipboard)
        {
            return;
        }
        // Validate with the same strict encoder the wire uses, so invalid
        // UTF-16 (a lone surrogate) is skipped here instead of throwing on
        // the send and taking the console down.
        if (!ClipboardText.TryEncode(text, out var payload) || payload.Length > ClipboardText.MaxBytes)
        {
            _lastClipboard = text; // don't re-attempt the same bad value every tick
            return;
        }
        _lastClipboard = text;
        RunOrCloseOnFailure(() => _client.SendClipboardBytes(payload));
    }

    // Puts the student's copied text on the teacher's clipboard.
    private void ApplyClipboard(string text)
    {
        if (text == _lastClipboard)
        {
            return;
        }
        _lastClipboard = text;
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception)
        {
            // Clipboard busy; the next change wins.
        }
    }

    // The wheel delta (one notch is +/-120) goes in X, read back on the
    // student's side; the cursor is already placed by the move stream.
    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        SendInput(InputEventKind.MouseWheel, e.Delta, 0, 0);
        e.Handled = true;
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

        var buffer = InputEventCodec.Encode(new InputEvent(kind, x, y, virtualKeyCode));
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
        SyncLockKeeper();
        ScreenImage.Focus();
    }

    private void ShowMode()
    {
        Title = $"Midview - {(_controlling ? "controlando" : "vendo")} {_machineName}";
        ControlButton.Content = _controlling ? "Parar de controlar (só ver)" : "Controlar";
        ModeTextBlock.Text = _controlling
            ? "Controlando: o teclado e o mouse do aluno estão travados."
            : "Só vendo: o aluno continua usando o computador normalmente.";
        CompatibilityNote.Visibility = _controlling && _endpoint == ControlEndpoint.Session
            ? Visibility.Visible
            : Visibility.Collapsed;
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
        _lockKeeper?.Dispose(); // gives the student the keyboard and mouse back
        _lockKeeper = null;
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
}
