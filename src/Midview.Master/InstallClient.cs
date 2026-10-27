using System.IO;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// Succeeded and Detail as the Agent said; OldAgent: a machine from before
// "Instalar programa", which installed nothing.
public sealed record InstallResult(bool Succeeded, string Detail, bool OldAgent = false);

// The Master's side of ProgramInstall, one machine at a time, always on the
// Agent's port with the Agent's thumbprint: only the Agent runs as SYSTEM, and
// students can read the Session's private key.
public static class InstallClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private const int ReplyTimeoutMs = 60_000;

    // progress: bytes sent so far and the total, while the file goes out.
    // uploaded: once the whole file is on the machine and its installer is
    // starting (the rest is waiting for it to end). port is for tests.
    public static InstallResult Install(
        KnownMachine machine, X509Certificate2 masterCertificate, string installerPath, string arguments,
        Action<long, long>? progress = null, Action? uploaded = null, int port = ConnectionClient.AgentPort)
    {
        var agentThumbprint = machine.AgentThumbprint is { Length: > 0 } thumbprint
            ? thumbprint
            : throw new InvalidOperationException("sem a identidade do Agent");
        var total = new FileInfo(installerPath).Length;
        var request = new InstallRequest(Path.GetFileName(installerPath), total, arguments.Trim());

        using var tcpClient = TcpConnect.WithTimeout(machine.HostAddress, port, ConnectTimeout);
        tcpClient.ReceiveTimeout = ReplyTimeoutMs;
        tcpClient.SendTimeout = ReplyTimeoutMs;
        using var channel = SecureChannel.AuthenticateAsClient(
            tcpClient, machine.HostAddress, masterCertificate, new TrustStore(new[] { agentThumbprint }));

        channel.Send(new Message(MessageType.InstallProgram, ProgramInstall.EncodeRequest(request)));
        var answer = channel.Receive();
        if (answer.Type == MessageType.CommandAck)
        {
            var (succeeded, detail) = CommandResult.Decode(answer.Payload);
            return !succeeded && detail.Length == 0
                ? new InstallResult(false, string.Empty, OldAgent: true)
                : new InstallResult(succeeded, detail);
        }
        if (answer.Type != MessageType.InstallAccepted)
        {
            throw new InvalidDataException($"resposta inesperada ({answer.Type})");
        }

        long sent = 0;
        using (var file = File.OpenRead(installerPath))
        {
            var buffer = new byte[FileTransfer.ChunkSize];
            int read;
            while (sent < total && (read = file.Read(buffer, 0, (int)Math.Min(buffer.Length, total - sent))) > 0)
            {
                channel.Send(new Message(MessageType.FileChunk, buffer[..read]));
                sent += read;
                progress?.Invoke(sent, total);
            }
        }
        if (sent != total)
        {
            throw new IOException("o instalador mudou de tamanho enquanto era enviado");
        }

        var started = channel.Receive();
        if (started.Type == MessageType.CommandAck)
        {
            var (succeeded, detail) = CommandResult.Decode(started.Payload);
            return new InstallResult(succeeded, detail);
        }
        if (started.Type != MessageType.InstallStarted)
        {
            throw new InvalidDataException($"resposta inesperada ({started.Type})");
        }

        uploaded?.Invoke();
        tcpClient.ReceiveTimeout = (int)ProgramInstall.MasterWait.TotalMilliseconds;
        CommandClient.EnableKeepAlive(tcpClient.Client);
        var reply = channel.Receive();
        if (reply.Type != MessageType.CommandAck)
        {
            throw new InvalidDataException($"resposta inesperada ({reply.Type})");
        }
        var (installed, installDetail) = CommandResult.Decode(reply.Payload);
        return new InstallResult(installed, installDetail);
    }
}
