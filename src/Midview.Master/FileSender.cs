using System.IO;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// Sends one file to one machine: header, wait to be accepted, then the file
// in FileTransfer.ChunkSize pieces, then wait for how it ended.
public static class FileSender
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    // Per read or write, not for the whole transfer: generous enough for a
    // busy lab network, short enough that a vanished machine fails.
    private const int TimeoutMs = 30_000;

    // Saved = the file is on the machine, and Detail is the name it was saved
    // under (it can differ, e.g. "notas (2).pdf"); otherwise Detail says why not.
    public static (bool Saved, string Detail) Send(
        KnownMachine machine, X509Certificate2 masterCertificate, string filePath,
        int port = ConnectionClient.SessionPort)
    {
        using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > FileTransfer.MaxFileSize)
        {
            return (false, "o arquivo passa de 1 GB");
        }

        using var tcpClient = TcpConnect.WithTimeout(machine.HostAddress, port, ConnectTimeout);
        tcpClient.ReceiveTimeout = TimeoutMs;
        tcpClient.SendTimeout = TimeoutMs;
        using var channel = SecureChannel.AuthenticateAsClient(
            tcpClient, machine.HostAddress, masterCertificate, new TrustStore(new[] { machine.SessionThumbprint }));

        channel.Send(new Message(MessageType.SendFile, FileTransfer.EncodeHeader(Path.GetFileName(filePath), file.Length)));
        var accepted = ReceiveResult(channel);
        if (!accepted.Saved)
        {
            return accepted;
        }

        var buffer = new byte[FileTransfer.ChunkSize];
        int read;
        while ((read = file.Read(buffer, 0, buffer.Length)) > 0)
        {
            channel.Send(new Message(MessageType.FileChunk, buffer.AsSpan(0, read).ToArray()));
        }

        return ReceiveResult(channel);
    }

    private static (bool Saved, string Detail) ReceiveResult(SecureChannel channel)
    {
        var reply = channel.Receive();
        return reply.Type == MessageType.CommandAck
            ? CommandResult.Decode(reply.Payload)
            : (false, $"resposta inesperada ({reply.Type})");
    }
}
