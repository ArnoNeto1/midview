using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// One command, one acknowledgement, over a short-lived connection: the
// Agent's site-blocking commands, and teacher messages sent to a Session.
public static class CommandClient
{
    // Without this, a machine that's switched off holds a command up for
    // Windows' own TCP connect timeout (~21 s).
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    // Covers a slow command on the Agent's side (a full internet block runs
    // several netsh calls) while still failing -- instead of leaving the
    // status on "Sending..." forever -- if the other side never answers.
    private const int TimeoutMs = 20_000;

    public static bool SendCommand(
        string hostAddress, int port, string targetThumbprint, X509Certificate2 masterCertificate,
        MessageType commandType, byte[] payload)
    {
        using var tcpClient = new TcpClient();
        using (var timeout = new CancellationTokenSource(ConnectTimeout))
        {
            tcpClient.ConnectAsync(hostAddress, port, timeout.Token).AsTask().GetAwaiter().GetResult();
        }
        tcpClient.ReceiveTimeout = TimeoutMs;
        tcpClient.SendTimeout = TimeoutMs;
        using var channel = SecureChannel.AuthenticateAsClient(
            tcpClient, hostAddress, masterCertificate, new TrustStore(new[] { targetThumbprint }));

        channel.Send(new Message(commandType, payload));
        var ack = channel.Receive();
        return ack.Type == MessageType.CommandAck && ack.Payload.Length > 0 && ack.Payload[0] == 1;
    }
}
