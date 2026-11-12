using System.IO;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// The Master's side of blocking apps on a phone, all on the phone agent's
// port 28900: ask what block it follows (GetPhoneAppBlock), apply one
// (SetPhoneAppBlock) and ask which apps the student can open (GetPhoneApps).
public static class PhoneAppBlockClient
{
    // Null Policy with OldAgent = true means a phone agent from before app
    // blocking (it answered CommandAck to a command it didn't know).
    public sealed record StatusResult(PhoneAppPolicy? Policy, bool OldAgent);

    public static StatusResult GetStatus(KnownMachine phone, X509Certificate2 masterCertificate, int port = ConnectionClient.AgentPort)
    {
        var reply = CommandClient.Ask(
            phone.HostAddress, port, AgentThumbprint(phone), masterCertificate, MessageType.GetPhoneAppBlock, Array.Empty<byte>());
        return reply.Type switch
        {
            MessageType.PhoneAppBlockStatus when PhoneAppPolicy.Decode(reply.Payload) is { } policy => new StatusResult(policy, false),
            MessageType.CommandAck => new StatusResult(null, true),
            _ => throw new InvalidDataException($"resposta inesperada ({reply.Type})"),
        };
    }

    public static (bool Succeeded, string Detail) Apply(
        KnownMachine phone, X509Certificate2 masterCertificate, PhoneAppPolicy policy, int port = ConnectionClient.AgentPort) =>
        CommandClient.SendCommandForResult(
            phone.HostAddress, port, AgentThumbprint(phone), masterCertificate, MessageType.SetPhoneAppBlock, policy.Encode());

    // The apps the student can open, with their icons; null for a phone
    // agent from before app blocking.
    public static IReadOnlyList<PhoneApp>? ListApps(
        KnownMachine phone, X509Certificate2 masterCertificate, int port = ConnectionClient.AgentPort)
    {
        var reply = CommandClient.Ask(
            phone.HostAddress, port, AgentThumbprint(phone), masterCertificate, MessageType.GetPhoneApps, Array.Empty<byte>());
        return reply.Type switch
        {
            MessageType.PhoneApps when PhoneAppList.Decode(reply.Payload) is { } apps => apps,
            MessageType.CommandAck => null,
            _ => throw new InvalidDataException($"resposta inesperada ({reply.Type})"),
        };
    }

    private static string AgentThumbprint(KnownMachine phone) =>
        phone.AgentThumbprint is { Length: > 0 } thumbprint
            ? thumbprint
            : throw new InvalidOperationException("sem a identidade do celular");
}
