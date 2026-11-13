using System.IO;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// "Otimizar" on one phone, on its agent's port 28900: OptimizePhone (nothing
// in it) answered with the phone's PhoneOptimizeReport. The phone hides the
// ad apps, tries the ad-free DNS, which can take ~40 s on a slow network
// (its own checks are capped), and uninstalls the apps the school does not
// use until 60 s after the question (the rest right after, in the
// background), so the answer is awaited for ReadTimeout.
public static class PhoneOptimizeClient
{
    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(90);

    // Null for a phone agent from before "Otimizar" (it answered CommandAck
    // to a command it didn't know).
    public static PhoneOptimizeReport? Optimize(
        KnownMachine phone, X509Certificate2 masterCertificate, int port = ConnectionClient.AgentPort)
    {
        var thumbprint = phone.AgentThumbprint is { Length: > 0 } known
            ? known
            : throw new InvalidOperationException("sem a identidade do celular");
        var reply = CommandClient.Ask(
            phone.HostAddress, port, thumbprint, masterCertificate, MessageType.OptimizePhone, Array.Empty<byte>(), ReadTimeout);
        return reply.Type switch
        {
            MessageType.PhoneOptimizeReport when PhoneOptimizeReport.TryDecode(reply.Payload, out var report) => report,
            MessageType.CommandAck => null,
            _ => throw new InvalidDataException($"resposta inesperada ({reply.Type})"),
        };
    }
}
