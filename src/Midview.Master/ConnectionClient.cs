using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

public sealed class ConnectionClient : IDisposable
{
    public const int SessionPort = 5901; // must match Midview.Session's listener
    public const int DeskPort = 5902; // must match Midview.Agent.AgentServerWorker.DeskPort
    public const int AgentPort = 28900; // must match Midview.Agent.AgentServerWorker.AgentPort

    private readonly SecureChannel _channel;

    private ConnectionClient(SecureChannel channel)
    {
        _channel = channel;
    }

    public static ConnectionClient Connect(
        string hostAddress, int port, string targetThumbprint, X509Certificate2 masterCertificate)
    {
        var tcpClient = new TcpClient();
        tcpClient.Connect(hostAddress, port);
        // Nagle's algorithm batches/delays small writes waiting to coalesce
        // them -- exactly wrong for this protocol's frequent tiny messages
        // (mouse moves, heartbeats, tile updates), adding real latency to
        // both directions regardless of how fast either side processes them.
        tcpClient.NoDelay = true;
        var channel = SecureChannel.AuthenticateAsClient(
            tcpClient, hostAddress, masterCertificate, new TrustStore(new[] { targetThumbprint }));
        return new ConnectionClient(channel);
    }

    // For a listener that may not be there (the Desk helper on a machine that
    // doesn't have it, or whose firewall drops the port): the connection and
    // the handshake each get at most this long, so the caller can fall back
    // in seconds instead of waiting out Windows' own ~21 s connect timeout.
    public static ConnectionClient Connect(
        string hostAddress, int port, string targetThumbprint, X509Certificate2 masterCertificate, TimeSpan timeout)
    {
        var tcpClient = TcpConnect.WithTimeout(hostAddress, port, timeout);
        try
        {
            tcpClient.NoDelay = true; // as in the overload above
            // Only the handshake is bounded: once it is done the receive loop
            // blocks on purpose, for as long as the screen stays quiet.
            tcpClient.ReceiveTimeout = (int)timeout.TotalMilliseconds;
            tcpClient.SendTimeout = (int)timeout.TotalMilliseconds;
            var channel = SecureChannel.AuthenticateAsClient(
                tcpClient, hostAddress, masterCertificate, new TrustStore(new[] { targetThumbprint }));
            tcpClient.ReceiveTimeout = 0;
            tcpClient.SendTimeout = 0;
            return new ConnectionClient(channel);
        }
        catch
        {
            tcpClient.Dispose();
            throw;
        }
    }

    // Must be the first message: asks for the screen as ScreenFrame updates
    // (see MessageType.ScreenStream).
    public void SendScreenStream() => _channel.Send(new Message(MessageType.ScreenStream, Array.Empty<byte>()));

    // Only ever sent on the helper connection (the 5902 channel), so the admin
    // password never travels on the Session. The payload is CredentialFill.Encode.
    public void SendFillAdminPassword(byte[] payload) => _channel.Send(new Message(MessageType.FillAdminPassword, payload));

    public void SendFrameAck(int frameId) => _channel.Send(new Message(MessageType.FrameAck, ScreenFrame.EncodeAck(frameId)));

    public void SendStartControl() => _channel.Send(new Message(MessageType.StartControl, Array.Empty<byte>()));

    public void SendStopControl() => _channel.Send(new Message(MessageType.StopControl, Array.Empty<byte>()));

    public void SendHeartbeat() => _channel.Send(new Message(MessageType.Heartbeat, Array.Empty<byte>()));

    public void SendInputEvent(byte[] encodedEvent) =>
        _channel.Send(new Message(MessageType.InputEvent, encodedEvent));

    public void SendClipboardText(string text) =>
        _channel.Send(new Message(MessageType.ClipboardText, ClipboardText.Encode(text)));

    public void SendClipboardBytes(byte[] payload) =>
        _channel.Send(new Message(MessageType.ClipboardText, payload));

    public Message ReceiveNext() => _channel.Receive();

    public void Dispose() => _channel.Dispose();
}
