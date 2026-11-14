using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// "Contas do aparelho" and "Sair das contas do Chrome" on one device, on its
// agent's port 28900: FreePhoneAccounts with the minutes (0 locks again
// now), or ClearChromeData, each answered with a CommandAck saying how it
// went.
public static class PhoneAccountsClient
{
    // The device waits for Android to erase Chrome's data (up to a minute)
    // before it answers.
    private static readonly TimeSpan ClearChromeTimeout = TimeSpan.FromSeconds(90);

    // Null for a device agent from before this (it fails a command it
    // doesn't know); otherwise the agent's answer.
    public static (bool Succeeded, string Detail)? Free(
        KnownMachine phone, int minutes, X509Certificate2 masterCertificate, int port = ConnectionClient.AgentPort) =>
        Send(phone, MessageType.FreePhoneAccounts, PhoneNetworkFree.Encode(minutes), masterCertificate, port, null);

    public static (bool Succeeded, string Detail)? ClearChrome(
        KnownMachine phone, X509Certificate2 masterCertificate, int port = ConnectionClient.AgentPort) =>
        Send(phone, MessageType.ClearChromeData, Array.Empty<byte>(), masterCertificate, port, ClearChromeTimeout);

    private static (bool Succeeded, string Detail)? Send(
        KnownMachine phone, MessageType type, byte[] payload, X509Certificate2 masterCertificate, int port, TimeSpan? timeout)
    {
        var thumbprint = phone.AgentThumbprint is { Length: > 0 } known
            ? known
            : throw new InvalidOperationException("sem a identidade do celular");
        var (succeeded, detail) = CommandClient.SendCommandForResult(
            phone.HostAddress, port, thumbprint, masterCertificate, type, payload, timeout);
        return PhonePinText.IsOldAgent(succeeded, detail) ? null : (succeeded, detail);
    }
}
