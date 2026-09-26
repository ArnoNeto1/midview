using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// One command, one acknowledgement, over a short-lived connection: the
// Agent's site-blocking and power commands, and teacher messages and "close
// programs" sent to a Session. Also one question, one answer (Ask).
public static class CommandClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    // Covers a slow command on the other side (a full internet block runs
    // several netsh calls; forcing programs closed waits 10 s for them first)
    // while still failing -- instead of leaving the status on "Sending..."
    // forever -- if the other side never answers.
    private const int TimeoutMs = 20_000;

    public static bool SendCommand(
        string hostAddress, int port, string targetThumbprint, X509Certificate2 masterCertificate,
        MessageType commandType, byte[] payload)
    {
        var ack = Exchange(hostAddress, port, targetThumbprint, masterCertificate, commandType, payload);
        return ack.Type == MessageType.CommandAck && ack.Payload.Length > 0 && ack.Payload[0] == 1;
    }

    // For questions answered with something other than a CommandAck (e.g.
    // the Agent's WakeAddress): the reply as it came.
    public static Message Ask(
        string hostAddress, int port, string targetThumbprint, X509Certificate2 masterCertificate,
        MessageType questionType, byte[] payload) =>
        Exchange(hostAddress, port, targetThumbprint, masterCertificate, questionType, payload);

    // For commands whose acknowledgement also says what happened (a
    // CommandResult), e.g. how many programs were asked to close.
    public static (bool Succeeded, string Detail) SendCommandForResult(
        string hostAddress, int port, string targetThumbprint, X509Certificate2 masterCertificate,
        MessageType commandType, byte[] payload)
    {
        var ack = Exchange(hostAddress, port, targetThumbprint, masterCertificate, commandType, payload);
        return ack.Type == MessageType.CommandAck
            ? CommandResult.Decode(ack.Payload)
            : (false, $"resposta inesperada ({ack.Type})");
    }

    private static Message Exchange(
        string hostAddress, int port, string targetThumbprint, X509Certificate2 masterCertificate,
        MessageType commandType, byte[] payload)
    {
        using var tcpClient = TcpConnect.WithTimeout(hostAddress, port, ConnectTimeout);
        tcpClient.ReceiveTimeout = TimeoutMs;
        tcpClient.SendTimeout = TimeoutMs;
        using var channel = SecureChannel.AuthenticateAsClient(
            tcpClient, hostAddress, masterCertificate, new TrustStore(new[] { targetThumbprint }));

        channel.Send(new Message(commandType, payload));
        return channel.Receive();
    }
}
