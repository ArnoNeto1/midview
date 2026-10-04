using System.IO;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// The Master's side of program blocking: ask a machine's Agent what it
// blocks (GetAppBlock, port 28900), apply a policy (SetAppBlock), and ask a
// machine's Session what the student has open (ListOpenPrograms, port 5901).
public static class AppBlockClient
{
    // Null Policy with OldAgent = true means an Agent from before this
    // feature (it answered CommandAck to a command it didn't know).
    public sealed record StatusResult(AppBlockPolicy? Policy, bool OldAgent);

    public static StatusResult GetStatus(KnownMachine machine, X509Certificate2 masterCertificate, int port = ConnectionClient.AgentPort)
    {
        var thumbprint = AgentThumbprint(machine);
        var reply = CommandClient.Ask(
            machine.HostAddress, port, thumbprint, masterCertificate, MessageType.GetAppBlock, Array.Empty<byte>());
        return reply.Type switch
        {
            MessageType.AppBlockStatus when AppBlockPolicy.TryDecode(reply.Payload, out var policy) => new StatusResult(policy, false),
            MessageType.CommandAck => new StatusResult(null, true),
            _ => throw new InvalidDataException($"resposta inesperada ({reply.Type})"),
        };
    }

    public static (bool Succeeded, string Detail) Apply(
        KnownMachine machine, X509Certificate2 masterCertificate, AppBlockPolicy policy, int port = ConnectionClient.AgentPort) =>
        CommandClient.SendCommandForResult(
            machine.HostAddress, port, AgentThumbprint(machine), masterCertificate, MessageType.SetAppBlock, policy.Encode());

    public static IReadOnlyList<OpenProgram> ListOpenPrograms(
        KnownMachine machine, X509Certificate2 masterCertificate, int port = ConnectionClient.SessionPort)
    {
        var reply = CommandClient.Ask(
            machine.HostAddress, port, machine.SessionThumbprint, masterCertificate, MessageType.ListOpenPrograms, Array.Empty<byte>());
        if (reply.Type == MessageType.OpenPrograms && OpenProgramsReport.TryDecode(reply.Payload, out var programs))
        {
            return programs;
        }
        throw new InvalidDataException($"resposta inesperada ({reply.Type})");
    }

    private static string AgentThumbprint(KnownMachine machine) =>
        machine.AgentThumbprint is { Length: > 0 } thumbprint
            ? thumbprint
            : throw new InvalidOperationException("sem a identidade do Agent");
}
