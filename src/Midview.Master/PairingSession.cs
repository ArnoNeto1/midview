using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Midview.Protocol;

namespace Midview.Master;

// The machine said no (Message is its reason, in Portuguese) or answered in
// a way pairing doesn't allow.
public sealed class PairingRefusedException(string message) : Exception(message);

// The teacher's side of pairing with one machine (see Pairing): Start runs
// the exchange up to the code; the teacher compares it with the one on the
// student's screen, then Confirm or Cancel. The connection stays open in
// between, for up to Pairing.ConfirmTimeout.
public sealed class PairingSession : IDisposable
{
    public const string InvalidAnswer = "A máquina respondeu de um jeito inválido ao pareamento. Tente de novo.";
    public const string OldAgent = "Midview antigo nesta máquina: atualize-a pela rede antes de parear.";

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private const int StepTimeoutMs = 15_000;

    private readonly TcpClient _tcpClient;
    private readonly SecureChannel _channel;

    private PairingSession(TcpClient tcpClient, SecureChannel channel, string address, int code)
    {
        _tcpClient = tcpClient;
        _channel = channel;
        Address = address;
        Code = code;
    }

    public string Address { get; }

    public string AgentThumbprint => _channel.RemoteThumbprint;

    public int Code { get; }

    public string CodeText => Pairing.Format(Code);

    public static PairingSession Start(string address, X509Certificate2 masterCertificate, string teacherName, int port = ConnectionClient.AgentPort)
    {
        var tcpClient = TcpConnect.WithTimeout(address, port, ConnectTimeout);
        SecureChannel? channel = null;
        try
        {
            tcpClient.ReceiveTimeout = StepTimeoutMs;
            tcpClient.SendTimeout = StepTimeoutMs;
            channel = SecureChannel.AuthenticateAsClientAcceptingAnyServer(tcpClient, address, masterCertificate);
            channel.Send(new Message(MessageType.PairRequest, Encoding.UTF8.GetBytes(teacherName)));
            var commitment = Expect(channel.Receive(), MessageType.PairCommit);

            var masterNonce = Pairing.NewNonce();
            channel.Send(new Message(MessageType.PairNonce, masterNonce));
            var agentNonce = Expect(channel.Receive(), MessageType.PairReveal);
            if (!Pairing.Opens(commitment, agentNonce))
            {
                throw new PairingRefusedException(InvalidAnswer);
            }

            var code = Pairing.Code(masterCertificate.Thumbprint, channel.RemoteThumbprint, agentNonce, masterNonce);
            return new PairingSession(tcpClient, channel, address, code);
        }
        catch
        {
            channel?.Dispose();
            tcpClient.Dispose();
            throw;
        }
    }

    // The teacher saw the same code on both screens.
    public KnownMachine Confirm()
    {
        _channel.Send(new Message(MessageType.PairConfirm, Array.Empty<byte>()));
        var result = PairingResult.Decode(Expect(_channel.Receive(), MessageType.PairDone))
            ?? throw new PairingRefusedException(InvalidAnswer);
        return new KnownMachine(
            result.Name, Address, result.SessionThumbprint.Trim().ToUpperInvariant(), AgentThumbprint,
            WakeOnLan.NormalizeMacAddress(result.MacAddress));
    }

    // The codes didn't match, or the teacher gave up: the machine takes its
    // code down and trusts nothing.
    public void Cancel()
    {
        try
        {
            _channel.Send(new Message(MessageType.PairCancel, Array.Empty<byte>()));
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
        {
            // Gone already; nothing was trusted.
        }
    }

    public void Dispose()
    {
        _channel.Dispose();
        _tcpClient.Dispose();
    }

    private static byte[] Expect(Message message, MessageType expected) => message.Type switch
    {
        _ when message.Type == expected => message.Payload,
        MessageType.PairRefused => throw new PairingRefusedException(Pairing.RefusalText(message.Payload)),
        MessageType.CommandAck => throw new PairingRefusedException(OldAgent),
        _ => throw new PairingRefusedException(InvalidAnswer),
    };
}
