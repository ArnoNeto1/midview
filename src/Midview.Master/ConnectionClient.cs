using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

public sealed class ConnectionClient : IDisposable
{
    public const int SessionPort = 5901; // must match Midview.Session's listener
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

    public void SendStartControl() => _channel.Send(new Message(MessageType.StartControl, Array.Empty<byte>()));

    public void SendStopControl() => _channel.Send(new Message(MessageType.StopControl, Array.Empty<byte>()));

    public void SendHeartbeat() => _channel.Send(new Message(MessageType.Heartbeat, Array.Empty<byte>()));

    public void SendInputEvent(byte[] encodedEvent) =>
        _channel.Send(new Message(MessageType.InputEvent, encodedEvent));

    public Message ReceiveNext() => _channel.Receive();

    public void Dispose() => _channel.Dispose();
}
