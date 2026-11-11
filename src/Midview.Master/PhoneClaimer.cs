using System.Security.Cryptography;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// The teacher's side of "ClaimPhone": a prepared phone that advertised the
// claim id of a QR this Master showed is asked to join. The token is never
// sent: the phone answers PairDone (as in PairingSession.Confirm) with a proof
// that it knows the token, which is checked here, or says no.
public static class PhoneClaimer
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private const int StepTimeoutMs = 15_000;

    public static KnownMachine Claim(
        string address, X509Certificate2 masterCertificate, string teacherName, string token, string number,
        int port = ConnectionClient.AgentPort)
    {
        using var tcpClient = TcpConnect.WithTimeout(address, port, ConnectTimeout);
        tcpClient.ReceiveTimeout = StepTimeoutMs;
        tcpClient.SendTimeout = StepTimeoutMs;
        using var channel = SecureChannel.AuthenticateAsClientAcceptingAnyServer(tcpClient, address, masterCertificate);
        // The token never goes on the wire: the phone proves it has it over
        // this nonce, bound to both certificates.
        var nonce = PhoneClaim.NewNonce();
        channel.Send(new Message(MessageType.ClaimPhone, new PhoneClaimRequest(nonce, teacherName).Encode()));

        var answer = channel.Receive();
        switch (answer.Type)
        {
            case MessageType.PairDone:
                var result = PairingResult.Decode(answer.Payload)
                    ?? throw new PairingRefusedException(PairingSession.InvalidAnswer);
                var expected = PhoneClaim.Proof(token, nonce, channel.RemoteThumbprint, masterCertificate.Thumbprint);
                if (result.Proof is null
                    || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(result.Proof), Encoding.UTF8.GetBytes(expected)))
                {
                    throw new PairingRefusedException(PairingSession.InvalidAnswer);
                }
                // The name comes from the claim's number, never from the phone:
                // a device holding the token for 07 must not be able to name
                // itself "Celular 03" and take that tile.
                return new KnownMachine(
                    PhoneNumber.NameOf(number), address, result.SessionThumbprint.Trim().ToUpperInvariant(), channel.RemoteThumbprint,
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
