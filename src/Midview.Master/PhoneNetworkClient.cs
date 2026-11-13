using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// "Liberar Wi-Fi e modo avião" on one phone, on its agent's port 28900:
// FreePhoneNetwork with the minutes (0 locks again now), or
// SetPhoneAdminPassword with the password's salted hash (empty removes it),
// each answered with a CommandAck saying how it went. The password itself
// never gets here: the window hashes it (PhoneAdminPassword.Encode).
public static class PhoneNetworkClient
{
    // Null for a phone agent from before this (it fails a command it doesn't
    // know); otherwise the agent's answer.
    public static (bool Succeeded, string Detail)? Free(
        KnownMachine phone, int minutes, X509Certificate2 masterCertificate, int port = ConnectionClient.AgentPort) =>
        Send(phone, MessageType.FreePhoneNetwork, PhoneNetworkFree.Encode(minutes), masterCertificate, port);

    // passwordPayload: PhoneAdminPassword.Encode's, or empty to remove the password.
    public static (bool Succeeded, string Detail)? SetAdminPassword(
        KnownMachine phone, byte[] passwordPayload, X509Certificate2 masterCertificate, int port = ConnectionClient.AgentPort)
    {
        if (passwordPayload.Length != 0 && PhoneAdminPassword.Decode(passwordPayload) is null)
        {
            throw new ArgumentException(PhoneAdminPassword.Invalid, nameof(passwordPayload));
        }
        return Send(phone, MessageType.SetPhoneAdminPassword, passwordPayload, masterCertificate, port);
    }

    private static (bool Succeeded, string Detail)? Send(
        KnownMachine phone, MessageType type, byte[] payload, X509Certificate2 masterCertificate, int port)
    {
        var thumbprint = phone.AgentThumbprint is { Length: > 0 } known
            ? known
            : throw new InvalidOperationException("sem a identidade do celular");
        var (succeeded, detail) = CommandClient.SendCommandForResult(
            phone.HostAddress, port, thumbprint, masterCertificate, type, payload);
        return PhoneNetworkText.IsOldAgent(succeeded, detail) ? null : (succeeded, detail);
    }
}
