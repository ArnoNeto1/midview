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
    internal const int TimeoutMs = 20_000;

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
    // CommandResult), e.g. how many programs were asked to close. timeout is
    // for a command known to take longer than TimeoutMs on the other side
    // (uninstalling programs); a machine that's switched off midway is still
    // noticed in a minute or two, by TCP keep-alive.
    public static (bool Succeeded, string Detail) SendCommandForResult(
        string hostAddress, int port, string targetThumbprint, X509Certificate2 masterCertificate,
        MessageType commandType, byte[] payload, TimeSpan? timeout = null)
    {
        var ack = Exchange(hostAddress, port, targetThumbprint, masterCertificate, commandType, payload, timeout);
        return ack.Type switch
        {
            MessageType.CommandAck => CommandResult.Decode(ack.Payload),
            MessageType.FrameTile => (false, OlderSession),
            _ => (false, $"resposta inesperada ({ack.Type})"),
        };
    }

    // A Session from before a command takes it for a control connection and
    // starts sending its screen instead of answering.
    public const string OlderSession = "Midview antigo nesta máquina: atualize-a para usar isto";

    private static Message Exchange(
        string hostAddress, int port, string targetThumbprint, X509Certificate2 masterCertificate,
        MessageType commandType, byte[] payload, TimeSpan? timeout = null)
    {
        using var tcpClient = TcpConnect.WithTimeout(hostAddress, port, ConnectTimeout);
        tcpClient.ReceiveTimeout = TimeoutMs;
        tcpClient.SendTimeout = TimeoutMs;
        using var channel = SecureChannel.AuthenticateAsClient(
            tcpClient, hostAddress, masterCertificate, new TrustStore(new[] { targetThumbprint }));

        channel.Send(new Message(commandType, payload));
        if (timeout is { } longWait)
        {
            tcpClient.ReceiveTimeout = (int)longWait.TotalMilliseconds;
            EnableKeepAlive(tcpClient.Client);
        }
        return channel.Receive();
    }

    // While waiting a long time for an answer, TCP keep-alive starts asking
    // whether the machine is still there after 30 s of quiet, and gives up
    // after 6 unanswered tries 10 s apart -- a machine switched off or
    // unplugged midway shows as "Sem resposta" in about a minute and a half,
    // not at the end of the whole wait. An older Windows that lacks the finer
    // settings keeps its defaults, and the timeout still ends the wait.
    internal static void EnableKeepAlive(Socket socket)
    {
        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 30);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 10);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 6);
        }
        catch (SocketException)
        {
        }
    }
}
