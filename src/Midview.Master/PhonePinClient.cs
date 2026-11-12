using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// The teacher's lock screen PIN on one phone, on its agent's port 28900:
// SetPhoneScreenPassword with the 4 digits (or nothing, to take it off),
// answered with a CommandAck saying how it went. The PIN is only passed
// through: nothing here keeps it.
public static class PhonePinClient
{
    // Null for a phone agent from before the PIN (it fails a command it
    // doesn't know); otherwise the agent's answer.
    public static (bool Succeeded, string Detail)? Send(
        KnownMachine phone, string pin, X509Certificate2 masterCertificate, int port = ConnectionClient.AgentPort)
    {
        var payload = PhoneScreenPassword.Encode(pin);
        var thumbprint = phone.AgentThumbprint is { Length: > 0 } known
            ? known
            : throw new InvalidOperationException("sem a identidade do celular");
        var (succeeded, detail) = CommandClient.SendCommandForResult(
            phone.HostAddress, port, thumbprint, masterCertificate, MessageType.SetPhoneScreenPassword, payload);
        return PhonePinText.IsOldAgent(succeeded, detail) ? null : (succeeded, detail);
    }
}
