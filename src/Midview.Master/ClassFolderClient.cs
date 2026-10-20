using System.IO;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// How one exchange with one machine went. Listing is what the machine's
// folder held at the end (null when it never said); NotLoaded means the
// machine holds another lesson, or none, and must be loaded first.
public sealed record ClassFolderResult(
    bool Succeeded, string Detail, IReadOnlyList<ClassFolderEntry>? Listing, bool NotLoaded = false, int Files = 0)
{
    public static ClassFolderResult Failed(string detail, bool notLoaded = false) => new(false, detail, null, notLoaded);
}

// The Master's side of the "Pasta da turma" exchanges with one machine (see
// ClassFolderSync), each over a short-lived connection to its Session.
public static class ClassFolderClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    // Per read or write, like FileSender: a machine deleting or listing a
    // big folder still answers well within it.
    private const int TimeoutMs = 60_000;

    // Loads the lesson onto a machine: tells it every file the teacher's
    // copy has, then sends the ones it asks for. A file that changed or went
    // since the copy was listed is skipped; the machine's listing at the end
    // says what it really has.
    public static ClassFolderResult Load(
        KnownMachine machine, X509Certificate2 masterCertificate, Guid lesson, string className,
        IReadOnlyList<ClassFolderFile> files, int port = ConnectionClient.SessionPort)
    {
        var manifest = files.Select(file => file.Entry).ToList();
        if (manifest.Count > ClassFolderSync.MaxEntries)
        {
            return ClassFolderResult.Failed($"a pasta da turma tem mais de {ClassFolderSync.MaxEntries} arquivos");
        }

        using var tcpClient = TcpConnect.WithTimeout(machine.HostAddress, port, ConnectTimeout);
        tcpClient.ReceiveTimeout = TimeoutMs;
        tcpClient.SendTimeout = TimeoutMs;
        using var channel = SecureChannel.AuthenticateAsClient(
            tcpClient, machine.HostAddress, masterCertificate, new TrustStore(new[] { machine.SessionThumbprint }));

        channel.Send(new Message(MessageType.LoadClassFolder, ClassFolderSync.EncodeRequest(lesson, className, manifest)));
        var reply = channel.Receive();
        if (Refusal(reply) is { } refusal)
        {
            return refusal;
        }
        if (reply.Type != MessageType.ClassFolderWanted
            || !ClassFolderSync.TryDecodeWanted(reply.Payload, manifest.Count, out var wanted))
        {
            return ClassFolderResult.Failed($"resposta inesperada ({reply.Type})");
        }

        var sent = 0;
        var buffer = new byte[FileTransfer.ChunkSize];
        foreach (var index in wanted)
        {
            if (SendIfUnchanged(channel, files[index], buffer))
            {
                sent++;
            }
        }
        channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(true, string.Empty)));

        var listingMessage = channel.Receive();
        if (listingMessage.Type != MessageType.ClassFolderListing
            || !ClassFolderSync.TryDecodeListing(listingMessage.Payload, out var listing))
        {
            return ClassFolderResult.Failed($"resposta inesperada ({listingMessage.Type})");
        }
        var ack = channel.Receive();
        if (ack.Type != MessageType.CommandAck)
        {
            return ClassFolderResult.Failed($"resposta inesperada ({ack.Type})");
        }
        var (succeeded, detail) = CommandResult.Decode(ack.Payload);
        return new ClassFolderResult(succeeded, detail, listing, Files: sent);
    }

    // Saves what changed on a machine: known is what it held the last time
    // (ClassLesson). Each file it sends is written to stagingDirectory and
    // handed to save, in the order it arrives, before the next one is read;
    // save returns null or why the file couldn't be saved. Received gets the
    // listed entry of every file that reached save.
    public static ClassFolderResult Collect(
        KnownMachine machine, X509Certificate2 masterCertificate, Guid lesson, IReadOnlyList<ClassFolderEntry> known,
        string stagingDirectory, Func<ClassFolderEntry, string, string?> save, ICollection<ClassFolderEntry> received,
        int port = ConnectionClient.SessionPort)
    {
        using var tcpClient = TcpConnect.WithTimeout(machine.HostAddress, port, ConnectTimeout);
        tcpClient.ReceiveTimeout = TimeoutMs;
        tcpClient.SendTimeout = TimeoutMs;
        using var channel = SecureChannel.AuthenticateAsClient(
            tcpClient, machine.HostAddress, masterCertificate, new TrustStore(new[] { machine.SessionThumbprint }));

        channel.Send(new Message(MessageType.CollectClassFolder, ClassFolderSync.EncodeRequest(lesson, string.Empty, known)));
        var reply = channel.Receive();
        if (Refusal(reply) is { } refusal)
        {
            return refusal;
        }
        if (reply.Type != MessageType.ClassFolderListing || !ClassFolderSync.TryDecodeListing(reply.Payload, out var listing))
        {
            return ClassFolderResult.Failed($"resposta inesperada ({reply.Type})");
        }

        // Only a listed file that isn't exactly one the Master named may
        // come, once, in listing order.
        var knownByPath = known.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        var expected = new Queue<ClassFolderEntry>(listing.Where(entry =>
            !(knownByPath.TryGetValue(entry.Path, out var seen) && seen.SameVersionAs(entry))));
        var problems = new List<string>();
        Directory.CreateDirectory(stagingDirectory);
        while (true)
        {
            var message = channel.Receive();
            if (message.Type == MessageType.CommandAck)
            {
                var (succeeded, detail) = CommandResult.Decode(message.Payload);
                return new ClassFolderResult(succeeded, Join(detail, problems), listing, Files: received.Count);
            }
            if (message.Type != MessageType.SendFile || !FileTransfer.TryDecodeHeader(message.Payload, out var path, out var size))
            {
                throw new InvalidDataException($"resposta inesperada ({message.Type})");
            }
            // Files listed as changed but skipped before this one are the
            // ones the machine couldn't read.
            ClassFolderEntry? next = null;
            while (next is null && expected.TryDequeue(out var candidate))
            {
                if (string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    next = candidate;
                }
            }
            if (next is null)
            {
                throw new InvalidDataException($"a máquina mandou um arquivo fora da lista: {path}");
            }

            var entry = next with { Size = size };
            var tempPath = Path.Combine(stagingDirectory, ClassFolderFiles.NewTempName());
            ReceiveTo(channel, tempPath, size, path);
            var problem = save(entry, tempPath);
            if (problem is null)
            {
                received.Add(entry);
            }
            else
            {
                problems.Add(problem);
            }
        }
    }

    // The end of the lesson: the machine's folder is emptied.
    public static (bool Succeeded, string Detail) Clear(
        KnownMachine machine, X509Certificate2 masterCertificate, int port = ConnectionClient.SessionPort) =>
        CommandClient.SendCommandForResult(
            machine.HostAddress, port, machine.SessionThumbprint, masterCertificate,
            MessageType.ClearClassFolder, Array.Empty<byte>());

    // A first answer that ends the exchange: a Session from before the
    // class folder (its screen), or a CommandAck saying why not.
    private static ClassFolderResult? Refusal(Message reply)
    {
        if (reply.Type == MessageType.FrameTile)
        {
            return ClassFolderResult.Failed(CommandClient.OlderSession);
        }
        if (reply.Type != MessageType.CommandAck)
        {
            return null;
        }
        var (_, detail) = CommandResult.Decode(reply.Payload);
        return ClassFolderResult.Failed(detail, notLoaded: detail == ClassFolderSync.NotLoaded);
    }

    // Sent only while the file is still the version listed, so what the
    // machine saves under that version really is it.
    private static bool SendIfUnchanged(SecureChannel channel, ClassFolderFile file, byte[] buffer)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        using (stream)
        {
            if (stream.Length != file.Entry.Size || File.GetLastWriteTimeUtc(file.FullPath).Ticks != file.Entry.LastWriteTicks)
            {
                return false;
            }
            channel.Send(new Message(MessageType.SendFile, FileTransfer.EncodeHeader(file.Entry.Path, file.Entry.Size)));
            for (var remaining = file.Entry.Size; remaining > 0;)
            {
                var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0)
                {
                    throw new IOException($"{file.Entry.Path} diminuiu durante o envio");
                }
                channel.Send(new Message(MessageType.FileChunk, buffer.AsSpan(0, read).ToArray()));
                remaining -= read;
            }
            return true;
        }
    }

    // All of it, or nothing left behind. A full disk stops the whole save
    // with a clear reason.
    private static void ReceiveTo(SecureChannel channel, string tempPath, long size, string path)
    {
        var free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(tempPath))!).AvailableFreeSpace;
        if (free < size)
        {
            throw new InvalidDataException("não há espaço livre suficiente neste computador");
        }

        var completed = false;
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                long received = 0;
                while (received < size)
                {
                    var chunk = channel.Receive();
                    if (chunk.Type != MessageType.FileChunk || chunk.Payload.Length == 0 || received + chunk.Payload.Length > size)
                    {
                        throw new InvalidDataException($"{path} chegou incompleto ou corrompido");
                    }
                    stream.Write(chunk.Payload);
                    received += chunk.Payload.Length;
                }
                stream.Flush(flushToDisk: true);
            }
            completed = true;
        }
        finally
        {
            if (!completed)
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static string Join(string detail, List<string> problems)
    {
        var parts = new List<string>();
        if (detail.Length > 0)
        {
            parts.Add(detail);
        }
        if (problems.Count > 0)
        {
            parts.Add($"{problems.Count} não salvo(s) aqui: {string.Join(", ", problems.Take(3))}{(problems.Count > 3 ? $" e mais {problems.Count - 3}" : string.Empty)}");
        }
        return string.Join("; ", parts);
    }
}
