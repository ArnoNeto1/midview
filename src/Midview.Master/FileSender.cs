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
    // under (it can differ, e.g. "notas (2).pdf"), with what happened next
    // when options were chosen; otherwise Detail says why not.
    public static (bool Saved, string Detail) Send(
        KnownMachine machine, X509Certificate2 masterCertificate, string filePath,
        SendFileOptions? options = null, int port = ConnectionClient.SessionPort)
    {
        options ??= SendFileOptions.Default;
        var result = SendOnce(machine, masterCertificate, filePath, options, port, out var olderSession);
        if (!olderSession)
        {
            return result;
        }

        // A Session from before the options took them for a control
        // connection: the file goes again, as it always did.
        var (saved, detail) = SendOnce(machine, masterCertificate, filePath, SendFileOptions.Default, port, out _);
        return (saved, saved ? $"{detail}; Midview antigo nesta máquina: sem as opções" : detail);
    }

    private static (bool Saved, string Detail) SendOnce(
        KnownMachine machine, X509Certificate2 masterCertificate, string filePath, SendFileOptions options, int port,
        out bool olderSession)
    {
        olderSession = false;
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

        var name = Path.GetFileName(filePath);
        channel.Send(options == SendFileOptions.Default
            ? new Message(MessageType.SendFile, FileTransfer.EncodeHeader(name, file.Length))
            : new Message(MessageType.SendFileWithOptions, FileTransfer.EncodeOptionsHeader(options, name, file.Length)));
        var reply = channel.Receive();
        if (reply.Type == MessageType.FrameTile)
        {
            olderSession = true;
            return (false, CommandClient.OlderSession);
        }
        (bool Saved, string Detail) accepted = reply.Type == MessageType.CommandAck
            ? CommandResult.Decode(reply.Payload)
            : (false, $"resposta inesperada ({reply.Type})");
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
