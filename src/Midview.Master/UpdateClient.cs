using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

public enum PushOutcome
{
    // The Agent has the zip, intact; the rest shows up in its status.
    Received,

    // Every byte went out but the answer didn't come back: poll the status
    // before concluding anything.
    SentNoAnswer,
    AlreadyInstalled,
    Refused,
    OldAgent,
}

public sealed record PushResult(PushOutcome Outcome, string Detail);

public sealed record StatusResult(UpdateStatusReport? Report, bool OldAgent);

// The Master's side of NetworkUpdate, one machine at a time, always on the
// Agent's port with the Agent's thumbprint (never the Session's: students
// can read the Session's private key).
public static class UpdateClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    // The Agent may take a while to answer an offer: it waits up to 30 s for
    // its own fingerprint and cleans up old builds first.
    private const int OfferReplyTimeoutMs = 90_000;
    private const int TransferTimeoutMs = 60_000;
    private const int StatusTimeoutMs = 20_000;

    // port is for tests.
    public static StatusResult GetStatus(
        KnownMachine machine, X509Certificate2 masterCertificate, byte[]? attemptId = null, int port = ConnectionClient.AgentPort)
    {
        using var tcpClient = Connect(machine, port, StatusTimeoutMs);
        using var channel = Authenticate(tcpClient, machine, masterCertificate);
        channel.Send(new Message(MessageType.GetUpdateStatus, attemptId ?? Array.Empty<byte>()));
        var reply = channel.Receive();
        return reply.Type switch
        {
            MessageType.UpdateStatus when UpdateStatusReport.TryDecode(reply.Payload, out var report) => new StatusResult(report, false),
            // An Agent from before network updates: its answer to an unknown command.
            MessageType.CommandAck => new StatusResult(null, true),
            _ => throw new InvalidDataException($"resposta inesperada ({reply.Type})"),
        };
    }

    public static PushResult Push(
        KnownMachine machine, X509Certificate2 masterCertificate, UpdatePackage package, byte[] attemptId,
        Action<long, long>? progress = null, CancellationToken cancellationToken = default, int port = ConnectionClient.AgentPort)
    {
        using var tcpClient = Connect(machine, port, OfferReplyTimeoutMs);
        using var channel = Authenticate(tcpClient, machine, masterCertificate);

        channel.Send(new Message(MessageType.UpdateOffer, NetworkUpdate.EncodeOffer(package.Offer(attemptId))));
        var answer = channel.Receive();
        if (answer.Type == MessageType.CommandAck)
        {
            var (succeeded, detail) = CommandResult.Decode(answer.Payload);
            return succeeded
                ? new PushResult(PushOutcome.AlreadyInstalled, detail)
                : detail.Length == 0
                    ? new PushResult(PushOutcome.OldAgent, string.Empty)
                    : new PushResult(PushOutcome.Refused, detail);
        }
        if (answer.Type != MessageType.UpdateAccepted)
        {
            throw new InvalidDataException($"resposta inesperada ({answer.Type})");
        }

        tcpClient.ReceiveTimeout = TransferTimeoutMs;
        tcpClient.SendTimeout = TransferTimeoutMs;
        var total = package.ZipSize;
        long sent = 0;
        using (var zip = File.OpenRead(package.ZipPath))
        {
            var buffer = new byte[FileTransfer.ChunkSize];
            int read;
            while ((read = zip.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                channel.Send(new Message(MessageType.FileChunk, buffer[..read]));
                sent += read;
                progress?.Invoke(sent, total);
            }
        }

        try
        {
            var reply = channel.Receive();
            var (received, detail) = CommandResult.Decode(reply.Payload);
            return reply.Type == MessageType.CommandAck && received
                ? new PushResult(PushOutcome.Received, detail)
                : new PushResult(PushOutcome.Refused, detail.Length > 0 ? detail : "a máquina recusou o pacote");
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or SocketException)
        {
            return new PushResult(PushOutcome.SentNoAnswer, ex.Message);
        }
    }

    private static TcpClient Connect(KnownMachine machine, int port, int timeoutMs)
    {
        var tcpClient = TcpConnect.WithTimeout(machine.HostAddress, port, ConnectTimeout);
        tcpClient.ReceiveTimeout = timeoutMs;
        tcpClient.SendTimeout = timeoutMs;
        return tcpClient;
    }

    private static SecureChannel Authenticate(TcpClient tcpClient, KnownMachine machine, X509Certificate2 masterCertificate)
    {
        var agentThumbprint = machine.AgentThumbprint is { Length: > 0 } thumbprint
            ? thumbprint
            : throw new InvalidOperationException("sem a identidade do Agent");
        return SecureChannel.AuthenticateAsClient(
            tcpClient, machine.HostAddress, masterCertificate, new TrustStore(new[] { agentThumbprint }));
    }
}
