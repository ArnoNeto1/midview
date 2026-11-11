using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// The teacher's side of "ClaimPhone": a prepared phone that advertised the
// claim id of a QR this Master showed is asked to join, with the QR's token as
// proof. The phone answers PairDone (as in PairingSession.Confirm) or says no.
public static class PhoneClaimer
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private const int StepTimeoutMs = 15_000;

    public static KnownMachine Claim(
        string address, X509Certificate2 masterCertificate, string teacherName, string token, int port = ConnectionClient.AgentPort)
    {
        using var tcpClient = TcpConnect.WithTimeout(address, port, ConnectTimeout);
        tcpClient.ReceiveTimeout = StepTimeoutMs;
        tcpClient.SendTimeout = StepTimeoutMs;
        using var channel = SecureChannel.AuthenticateAsClientAcceptingAnyServer(tcpClient, address, masterCertificate);
        channel.Send(new Message(MessageType.ClaimPhone, new PhoneClaimRequest(token, teacherName).Encode()));

        var answer = channel.Receive();
        switch (answer.Type)
        {
            case MessageType.PairDone:
                var result = PairingResult.Decode(answer.Payload)
                    ?? throw new PairingRefusedException(PairingSession.InvalidAnswer);
                return new KnownMachine(
                    result.Name, address, result.SessionThumbprint.Trim().ToUpperInvariant(), channel.RemoteThumbprint,
                    WakeOnLan.NormalizeMacAddress(result.MacAddress), result.DeviceKind);
            case MessageType.PairRefused:
                throw new PairingRefusedException(Pairing.RefusalText(answer.Payload));
            case MessageType.CommandAck:
                throw new PairingRefusedException(PairingSession.OldAgent);
            default:
                throw new PairingRefusedException(PairingSession.InvalidAnswer);
        }
    }
}
