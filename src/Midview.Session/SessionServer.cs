using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Session;

internal static class InputEventCodec
{
    public static InputEvent Decode(byte[] payload)
    {
        var kind = (InputEventKind)payload[0];
        var x = BitConverter.ToInt32(payload, 1);
        var y = BitConverter.ToInt32(payload, 5);
        var vk = BitConverter.ToUInt16(payload, 9);
        return new InputEvent(kind, x, y, vk);
    }

    public static byte[] Encode(InputEvent inputEvent)
    {
        var buffer = new byte[11];
        buffer[0] = (byte)inputEvent.Kind;
        BitConverter.GetBytes(inputEvent.X).CopyTo(buffer, 1);
        BitConverter.GetBytes(inputEvent.Y).CopyTo(buffer, 5);
        BitConverter.GetBytes(inputEvent.VirtualKeyCode).CopyTo(buffer, 9);
        return buffer;
    }
}

internal static class TileMessageCodec
{
    public static byte[] Encode(EncodedTile tile)
    {
        var buffer = new byte[8 + tile.JpegBytes.Length];
        BitConverter.GetBytes(tile.Row).CopyTo(buffer, 0);
        BitConverter.GetBytes(tile.Column).CopyTo(buffer, 4);
        tile.JpegBytes.CopyTo(buffer, 8);
        return buffer;
    }

    public static (int Row, int Column, byte[] JpegBytes) Decode(byte[] payload)
    {
        var row = BitConverter.ToInt32(payload, 0);
        var column = BitConverter.ToInt32(payload, 4);
        var jpegBytes = payload[8..];
        return (row, column, jpegBytes);
    }
}

public sealed class SessionServer
{
    private const int HeartbeatCheckIntervalMs = 1000;

    // Capture cadence is fully decoupled from message traffic (mouse-move
    // events alone can arrive far faster than any capture pipeline should
    // run). ~15 fps matches Veyon's own real-world capture rate.
    private const int CaptureIntervalMs = 66;

    // The classroom grid's tile size in MidviewMaster; frames are scaled to
    // fit inside it, keeping their aspect ratio.
    private const int ThumbnailMaxWidth = 320;
    private const int ThumbnailMaxHeight = 180;

    private readonly TcpListener _listener;
    private readonly X509Certificate2 _serverCertificate;
    private readonly TrustStore _masterTrustStore;
    private readonly IScreenCapture _screenCapture;
    private readonly IInputInjector _inputInjector;
    private readonly InputLockManager _inputLockManager;

    public SessionServer(
        TcpListener listener, X509Certificate2 serverCertificate, TrustStore masterTrustStore,
        IScreenCapture screenCapture, IInputInjector inputInjector, InputLockManager inputLockManager)
    {
        _listener = listener;
        _serverCertificate = serverCertificate;
        _masterTrustStore = masterTrustStore;
        _screenCapture = screenCapture;
        _inputInjector = inputInjector;
        _inputLockManager = inputLockManager;
    }

    public void Run(CancellationToken cancellationToken)
    {
        _listener.Start();
        while (!cancellationToken.IsCancellationRequested)
        {
            var tcpClient = _listener.AcceptTcpClient();
            // Nagle's algorithm batches/delays small writes waiting to
            // coalesce them -- exactly wrong for this protocol's frequent
            // tiny messages (mouse moves, heartbeats, tile updates), adding
            // real latency to both directions regardless of how fast either
            // side processes them.
            tcpClient.NoDelay = true;

            // Each connection gets its own thread: MidviewMaster keeps a
            // long-lived thumbnail connection open to every machine in its
            // classroom grid, so handling one client at a time would leave
            // a full-control connection waiting behind it forever.
            var clientThread = new Thread(() =>
            {
                try
                {
                    HandleClient(tcpClient, cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error handling a client connection: {ex.Message}");
                }
            }) { IsBackground = true };
            clientThread.Start();
        }
    }

    private void HandleClient(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        using var client = tcpClient;
        using var channel = SecureChannel.AuthenticateAsServer(client, _serverCertificate, _masterTrustStore);
        var isControlling = false;

        // Started by the first StartControl, never before: a classroom-grid
        // preview connection only ever asks for thumbnails, so it must not
        // pay for a ~15 fps capture thread or a heartbeat watchdog.
        ControlFeed? controlFeed = null;

        try
        {
            var messageCount = 0;
            var maxGapMs = 0.0;
            var gapSw = System.Diagnostics.Stopwatch.StartNew();

            while (!cancellationToken.IsCancellationRequested && tcpClient.Connected)
            {
                var receiveSw = System.Diagnostics.Stopwatch.StartNew();
                var message = channel.Receive();
                var receiveMs = receiveSw.Elapsed.TotalMilliseconds;

                var gapMs = gapSw.Elapsed.TotalMilliseconds;
                gapSw.Restart();
                maxGapMs = Math.Max(maxGapMs, gapMs);
                messageCount++;
                if (messageCount % 50 == 0)
                {
                    SessionDiagnostics.Log(
                        $"messages received={messageCount} lastReceiveBlockedFor={receiveMs:F1}ms maxGapSinceLast50={maxGapMs:F1}ms");
                    maxGapMs = 0.0;
                }

                switch (message.Type)
                {
                    case MessageType.StartControl:
                        isControlling = true;
                        _inputLockManager.StartLock();
                        controlFeed ??= new ControlFeed(_screenCapture, channel, _inputLockManager, cancellationToken);
                        break;

                    case MessageType.StopControl:
                        isControlling = false;
                        _inputLockManager.StopLock();
                        break;

                    case MessageType.Heartbeat:
                        _inputLockManager.OnHeartbeatReceived();
                        break;

                    case MessageType.InputEvent:
                        if (isControlling)
                        {
                            _inputInjector.Inject(InputEventCodec.Decode(message.Payload));
                        }
                        break;

                    // Only on a connection with no capture thread: that thread
                    // is then this channel's one writer, and SslStream doesn't
                    // support two threads writing at once.
                    case MessageType.RequestThumbnail when controlFeed is null:
                        SendThumbnail(channel);
                        break;
                }
            }
        }
        finally
        {
            controlFeed?.Dispose();
            // Safety net for this connection's own lock only: a preview
            // connection closing must not unlock input that a separate
            // full-control connection to this machine is still holding.
            if (isControlling)
            {
                _inputLockManager.StopLock();
            }
        }
    }

    private void SendThumbnail(SecureChannel channel)
    {
        byte[] jpegBytes;
        try
        {
            using var frame = _screenCapture.CaptureFrame();
            jpegBytes = ThumbnailRenderer.Render(frame, ThumbnailMaxWidth, ThumbnailMaxHeight);
        }
        catch (Exception ex)
        {
            // Not rethrown: one failed capture shouldn't end the connection.
            // No reply is sent; the Master's poll times out and shows this
            // machine as offline until its next attempt.
            Console.WriteLine($"Thumbnail capture failed: {ex.Message}");
            return;
        }

        channel.Send(new Midview.Protocol.Message(MessageType.ThumbnailFrame, jpegBytes));
    }

    // One connection's full-control feed: the fixed-cadence capture thread
    // streaming FrameTiles, plus the watchdog that releases the input lock
    // if the Master stops sending heartbeats.
    private sealed class ControlFeed : IDisposable
    {
        private readonly System.Threading.Timer _heartbeatTimer;
        private readonly CancellationTokenSource _captureCts;
        private readonly Thread _captureThread;

        public ControlFeed(
            IScreenCapture screenCapture, SecureChannel channel, InputLockManager inputLockManager,
            CancellationToken cancellationToken)
        {
            _heartbeatTimer = new System.Threading.Timer(_ => inputLockManager.CheckHeartbeatTimeout(),
                null, HeartbeatCheckIntervalMs, HeartbeatCheckIntervalMs);

            // A fresh FramePipeline per connection: it isn't thread-safe, and
            // reusing one server-wide let a slow-to-stop capture thread from a
            // previous connection race a new one over the same mutable state
            // after a reconnect (also left the diff starting from a stale frame).
            var framePipeline = new FramePipeline(screenCapture);

            // The capture loop runs on its own thread at a fixed cadence,
            // independent of how often messages arrive on the connection.
            // The two threads never touch the channel at the same time from
            // opposite ends of a single call: this thread only sends
            // (FrameTile), HandleClient's loop only receives -- the one
            // reader/one writer pattern SslStream supports without locking.
            _captureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _captureThread = new Thread(() => CaptureLoop(framePipeline, channel, _captureCts)) { IsBackground = true };
            _captureThread.Start();
        }

        public void Dispose()
        {
            _captureCts.Cancel();
            _captureThread.Join(TimeSpan.FromSeconds(2));
            _heartbeatTimer.Dispose();
            _captureCts.Dispose();
        }

        private static void CaptureLoop(FramePipeline framePipeline, SecureChannel channel, CancellationTokenSource captureCts)
        {
            try
            {
                while (!captureCts.IsCancellationRequested)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();

                    foreach (var tile in framePipeline.CaptureNextFrame())
                    {
                        channel.Send(new Midview.Protocol.Message(MessageType.FrameTile, TileMessageCodec.Encode(tile)));
                    }

                    var remaining = CaptureIntervalMs - (int)sw.ElapsedMilliseconds;
                    if (remaining > 0)
                    {
                        captureCts.Token.WaitHandle.WaitOne(remaining);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"Capture loop stopped: {ex.Message}");
                captureCts.Cancel(); // connection is likely dead; let the receive loop wind down too
            }
        }
    }
}
