using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Midview.Protocol;

namespace Midview.Master;

// The Master's side of the lab-configuration panel: ask a machine's Agent
// what it has applied (GetLabConfig, port 28900), apply a configuration
// (SetLabConfig), and reset a machine (ResetLabConfig).
public static class LabConfigClient
{
    // Null Config with OldAgent = true means an Agent from before this
    // feature (it answered CommandAck to GetLabConfig, which it didn't know).
    public sealed record StatusResult(LabConfig? Config, bool OldAgent);

    public static StatusResult GetStatus(KnownMachine machine, X509Certificate2 masterCertificate, int port = ConnectionClient.AgentPort)
    {
        var reply = CommandClient.Ask(
            machine.HostAddress, port, AgentThumbprint(machine), masterCertificate, MessageType.GetLabConfig, Array.Empty<byte>());
        return reply.Type switch
        {
            MessageType.LabConfigStatus when LabConfig.TryDecode(reply.Payload, out var config) => new StatusResult(config, false),
            MessageType.CommandAck => new StatusResult(null, true),
            _ => throw new InvalidDataException($"resposta inesperada ({reply.Type})"),
        };
    }

    public static (bool Succeeded, string Detail) Apply(
        KnownMachine machine, X509Certificate2 masterCertificate, LabConfig config, int port = ConnectionClient.AgentPort) =>
        CommandClient.SendCommandForResult(
            machine.HostAddress, port, AgentThumbprint(machine), masterCertificate, MessageType.SetLabConfig, config.Encode());

    public static (bool Succeeded, string Detail) Reset(
        KnownMachine machine, X509Certificate2 masterCertificate, int port = ConnectionClient.AgentPort) =>
        CommandClient.SendCommandForResult(
            machine.HostAddress, port, AgentThumbprint(machine), masterCertificate, MessageType.ResetLabConfig, Array.Empty<byte>());

    public static (bool Succeeded, string Detail) ClearChromeProfiles(
        KnownMachine machine, X509Certificate2 masterCertificate, int port = ConnectionClient.AgentPort) =>
        CommandClient.SendCommandForResult(
            machine.HostAddress, port, AgentThumbprint(machine), masterCertificate, MessageType.ClearChromeProfiles, Array.Empty<byte>());

    // The usual distraction sites, blocked or freed through the site-blocking
    // the Agent already has (the hosts file, so it holds in any browser).
    public static readonly IReadOnlyList<string> DistractionSites = new[]
    {
        "facebook.com", "instagram.com", "tiktok.com", "twitter.com", "x.com", "youtube.com", "roblox.com", "poki.com",
    };

    public static (bool Succeeded, string Detail) SetDistractionSites(
        KnownMachine machine, X509Certificate2 masterCertificate, bool block, int port = ConnectionClient.AgentPort)
    {
        var command = block ? MessageType.BlockDomain : MessageType.UnblockDomain;
        // Every domain is attempted, so one failure doesn't leave the rest
        // unattempted or misreport the ones already changed.
        var failed = new List<string>();
        foreach (var domain in DistractionSites)
        {
            if (!CommandClient.SendCommand(
                machine.HostAddress, port, AgentThumbprint(machine), masterCertificate, command, Encoding.UTF8.GetBytes(domain)))
            {
                failed.Add(domain);
            }
        }

        var verb = block ? "bloqueados" : "liberados";
        return failed.Count == 0
            ? (true, $"sites de distração {verb}")
            : (false, $"sites de distração {verb} em {DistractionSites.Count - failed.Count} de {DistractionSites.Count}; faltou: {string.Join(", ", failed)}");
    }

    public static (bool Succeeded, string Detail) ClearDownloads(
        KnownMachine machine, X509Certificate2 masterCertificate, DownloadsScope scope, int port = ConnectionClient.AgentPort) =>
        CommandClient.SendCommandForResult(
            machine.HostAddress, port, AgentThumbprint(machine), masterCertificate,
            MessageType.ClearDownloads, new[] { (byte)scope });

    // Null Programs with OldAgent = true means an Agent from before this
    // feature (it answered CommandAck to GetInstalledPrograms).
    public sealed record InstalledResult(IReadOnlyList<string>? Programs, bool OldAgent);

    public static InstalledResult GetInstalledPrograms(
        KnownMachine machine, X509Certificate2 masterCertificate, int port = ConnectionClient.AgentPort)
    {
        var reply = CommandClient.Ask(
            machine.HostAddress, port, AgentThumbprint(machine), masterCertificate, MessageType.GetInstalledPrograms, Array.Empty<byte>());
        return reply.Type switch
        {
            MessageType.InstalledPrograms when ProgramList.TryDecode(reply.Payload, out var programs) => new InstalledResult(programs, false),
            MessageType.CommandAck => new InstalledResult(null, true),
            _ => throw new InvalidDataException($"resposta inesperada ({reply.Type})"),
        };
    }

    public static (bool Succeeded, string Detail) UninstallPrograms(
        KnownMachine machine, X509Certificate2 masterCertificate, IReadOnlyList<string> displayNames, int port = ConnectionClient.AgentPort) =>
        CommandClient.SendCommandForResult(
            machine.HostAddress, port, AgentThumbprint(machine), masterCertificate,
            MessageType.UninstallPrograms, ProgramList.Encode(displayNames), UninstallTimeout(displayNames.Count));

    // How long a machine may take to answer an uninstall: it answers only once
    // every program is done, and gives each uninstaller up to 5 minutes (a big
    // one like Office takes several), sometimes two for one program (its 64-
    // and 32-bit entries) -- so 10 minutes a program, on top of the usual 20 s.
    public static TimeSpan UninstallTimeout(int programCount) =>
        TimeSpan.FromSeconds(20) + TimeSpan.FromMinutes(10) * Math.Max(1, programCount);

    private static string AgentThumbprint(KnownMachine machine) =>
        machine.AgentThumbprint is { Length: > 0 } thumbprint
            ? thumbprint
            : throw new InvalidOperationException("sem a identidade do Agent");
}
