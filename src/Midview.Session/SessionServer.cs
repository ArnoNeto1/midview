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

    private const int CaptureRetryDelayMs = 500;

    // Plenty for a real LAN handshake. Anything on the network can open a
    // TCP connection and then send nothing; without a timeout that alone
    // kept every Master out.
    private const int HandshakeTimeoutMs = 10_000;

    // The Master sends a heartbeat every second for as long as its window is
    // open, so this much silence means it's gone (powered off, cable pulled)
    // without closing the connection. Without a timeout, Receive() would wait
    // forever and no Master could ever connect again. Also how long a new
    // Master waits for a previous connection to end before giving up.
    private const int ConnectionTimeoutMs = 15_000;

    private readonly TcpListener _listener;
    private readonly X509Certificate2 _serverCertificate;
    private readonly TrustStore _masterTrustStore;
    private readonly IScreenCapture _screenCapture;
    private readonly IInputInjector _inputInjector;
    private readonly InputLockManager _inputLockManager;

    // One Master served at a time: the input lock and the injected input
    // belong to the machine, not to a connection.
    private readonly SemaphoreSlim _servingSlot = new(1, 1);

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
            // Off the accept loop, so a connection stuck in its TLS handshake
            // can't keep the next one from being accepted.
            _ = Task.Run(() => HandleClientSafely(tcpClient, cancellationToken));
        }
    }

    private void HandleClientSafely(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        try
        {
            HandleClient(tcpClient, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling a client connection: {ex.Message}");
        }
    }

    private void HandleClient(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        using var client = tcpClient;
        client.ReceiveTimeout = HandshakeTimeoutMs;
        client.SendTimeout = HandshakeTimeoutMs;
        using var channel = SecureChannel.AuthenticateAsServer(client, _serverCertificate, _masterTrustStore);

        if (!_servingSlot.Wait(ConnectionTimeoutMs, cancellationToken))
        {
            Console.WriteLine("Another MidviewMaster is still connected; closing the new connection.");
            return;
        }

        try
        {
            client.ReceiveTimeout = ConnectionTimeoutMs;
            client.SendTimeout = ConnectionTimeoutMs;
            Serve(client, channel, cancellationToken);
        }
        finally
        {
            _servingSlot.Release();
        }
    }

    private void Serve(TcpClient tcpClient, SecureChannel channel, CancellationToken cancellationToken)
    {
        var isControlling = false;

        // An exception escaping a Timer callback would take the whole process down.
        using var heartbeatTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                _inputLockManager.CheckHeartbeatTimeout();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Heartbeat check failed: {ex.Message}");
            }
        }, null, HeartbeatCheckIntervalMs, HeartbeatCheckIntervalMs);

        // A fresh FramePipeline per connection: it isn't thread-safe, and
        // reusing one server-wide let a slow-to-stop capture thread from a
        // previous connection race a new one over the same mutable state
        // after a reconnect (also left the diff starting from a stale frame).
        var framePipeline = new FramePipeline(_screenCapture);

        // The capture loop runs on its own thread at a fixed cadence,
        // independent of how often messages arrive on the connection below.
        // The two threads never touch the channel at the same time from
        // opposite ends of a single call: this thread only sends
        // (FrameTile), the loop below only receives -- the one
        // reader/one writer pattern SslStream supports without locking.
        using var captureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var captureThread = new Thread(() => CaptureLoop(framePipeline, channel, tcpClient, captureCts)) { IsBackground = true };
        captureThread.Start();

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
                }
            }
        }
        finally
        {
            captureCts.Cancel();
            captureThread.Join(TimeSpan.FromSeconds(2));
            _inputLockManager.StopLock(); // safety net: connection ended for any reason
        }
    }

    private void CaptureLoop(
        FramePipeline framePipeline, SecureChannel channel, TcpClient tcpClient, CancellationTokenSource captureCts)
    {
        try
        {
            while (!captureCts.IsCancellationRequested)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();

                IReadOnlyList<EncodedTile> tiles;
                try
                {
                    tiles = framePipeline.CaptureNextFrame();
                }
                catch (Exception ex)
                {
                    // Capturing fails while the secure desktop is up (Win+L,
                    // a UAC prompt, Ctrl+Alt+Del) and works again once it's
                    // gone. Retrying keeps the stream alive; giving up would
                    // leave the teacher looking at a frozen screen with no
                    // error, since the connection itself stays open.
                    Console.WriteLine($"Screen capture failed, retrying: {ex.Message}");
                    captureCts.Token.WaitHandle.WaitOne(CaptureRetryDelayMs);
                    continue;
                }

                foreach (var tile in tiles)
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
            // Only a failed send gets here, so the connection is dead. Closing
            // the socket is what actually ends the receive loop's blocked
            // Receive() -- it isn't watching any cancellation token.
            Console.WriteLine($"Capture loop stopped: {ex.Message}");
            tcpClient.Close();
        }
    }
}
