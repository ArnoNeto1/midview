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

    private readonly TcpListener _listener;
    private readonly X509Certificate2 _serverCertificate;
    private readonly TrustStore _masterTrustStore;
    private readonly FramePipeline _framePipeline;
    private readonly IInputInjector _inputInjector;
    private readonly InputLockManager _inputLockManager;

    public SessionServer(
        TcpListener listener, X509Certificate2 serverCertificate, TrustStore masterTrustStore,
        FramePipeline framePipeline, IInputInjector inputInjector, InputLockManager inputLockManager)
    {
        _listener = listener;
        _serverCertificate = serverCertificate;
        _masterTrustStore = masterTrustStore;
        _framePipeline = framePipeline;
        _inputInjector = inputInjector;
        _inputLockManager = inputLockManager;
    }

    public void Run(CancellationToken cancellationToken)
    {
        _listener.Start();
        while (!cancellationToken.IsCancellationRequested)
        {
            var tcpClient = _listener.AcceptTcpClient();
            try
            {
                HandleClient(tcpClient, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling a client connection: {ex.Message}");
            }
        }
    }

    private void HandleClient(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        using var client = tcpClient;
        using var channel = SecureChannel.AuthenticateAsServer(client, _serverCertificate, _masterTrustStore);
        var isControlling = false;

        using var heartbeatTimer = new System.Threading.Timer(_ => _inputLockManager.CheckHeartbeatTimeout(),
            null, HeartbeatCheckIntervalMs, HeartbeatCheckIntervalMs);

        try
        {
            while (!cancellationToken.IsCancellationRequested && tcpClient.Connected)
            {
                var message = channel.Receive();
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

                foreach (var tile in _framePipeline.CaptureNextFrame())
                {
                    channel.Send(new Midview.Protocol.Message(MessageType.FrameTile, TileMessageCodec.Encode(tile)));
                }
            }
        }
        finally
        {
            _inputLockManager.StopLock(); // safety net: connection ended for any reason
        }
    }
}
