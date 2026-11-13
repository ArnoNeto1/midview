using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// "Tirar o Midview dos celulares e tablets" on one device, on its agent's
// port 28900: RemoveMidviewFromPhone with the word the teacher typed
// ("Desfazer e tirar o Midview"), or WipePhone ("Restaurar de fábrica"),
// each answered with a CommandAck saying how it went.
public static class PhoneRemoveClient
{
    // The phone lifts its locks and brings its system apps back before it
    // answers: up to a minute on a slow phone.
    private static readonly TimeSpan LeaveTimeout = TimeSpan.FromMinutes(3);

    // Null for a phone agent from before the command (it fails a command it
    // doesn't know); otherwise the agent's answer.
    public static (bool Succeeded, string Detail)? Send(
        KnownMachine phone, PhoneRemoveChoice choice, X509Certificate2 masterCertificate, int port = ConnectionClient.AgentPort)
    {
        var thumbprint = phone.AgentThumbprint is { Length: > 0 } known
            ? known
            : throw new InvalidOperationException("sem a identidade do celular");
        var (succeeded, detail) = choice == PhoneRemoveChoice.FactoryReset
            ? CommandClient.SendCommandForResult(
                phone.HostAddress, port, thumbprint, masterCertificate, MessageType.WipePhone, Array.Empty<byte>())
            : CommandClient.SendCommandForResult(
                phone.HostAddress, port, thumbprint, masterCertificate, MessageType.RemoveMidviewFromPhone, PhoneLeave.Encode(), LeaveTimeout);
        return PhoneRemoveText.IsOldAgent(succeeded, detail) ? null : (succeeded, detail);
    }
}
