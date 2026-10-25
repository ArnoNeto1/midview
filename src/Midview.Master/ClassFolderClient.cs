using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Midview.Protocol;

namespace Midview.Master;

// The Master's side of the class folder ("Pasta da turma", see ClassLesson,
// ClassFolderWire and docs/superpowers/specs/2026-10-20-class-folder-design.md):
// the lesson and the class folders' home (ClassHome, for two teacher PCs in one
// lab) go to a machine's Agent (port 28900), and a student's Session (port
// 5901) lists, hands over, takes and changes the files of the folder. One
// short-lived connection per call.
//
// What a machine answers is hostile input: a file read from a Session is
// checked against its own header (never more than FileTransfer.MaxFileSize,
// never a byte past the size it announced, never fewer) before it counts as
// received. Failures the other side or this PC's own disk can explain come
// back as (false, why), in Portuguese, for the teacher; a connection that
// fails or drops throws, as in the other clients (FailureText words it), and
// so does a call for a machine with no Agent identity.
public static class ClassFolderClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    // Per read or write, not for the whole transfer: like FileSender.
    private const int TimeoutMs = 30_000;

    private const string NotSendable = "o nome do arquivo não pode ser enviado";

    // An Agent from before the class folder doesn't know SetClassLesson and
    // answers a bare CommandAck, which decodes to (false, ""): said the way a
    // Session from before it is, since the fix is the same.
    public static (bool Succeeded, string Detail) SetLesson(
        KnownMachine machine, X509Certificate2 masterCertificate, ClassLesson lesson, int port = ConnectionClient.AgentPort)
    {
        var (succeeded, detail) = CommandClient.SendCommandForResult(
            machine.HostAddress, port, AgentThumbprint(machine), masterCertificate, MessageType.SetClassLesson, lesson.Encode());
        return !succeeded && detail.Length == 0 ? (false, CommandClient.OlderSession) : (succeeded, detail);
    }

    // The lesson the machine's Agent follows (GetClassLesson); OldAgent, with
    // no lesson, for an Agent from before the class folder.
    public static (ClassLesson? Lesson, bool OldAgent) GetLesson(
        KnownMachine machine, X509Certificate2 masterCertificate, int port = ConnectionClient.AgentPort)
    {
        var reply = CommandClient.Ask(
            machine.HostAddress, port, AgentThumbprint(machine), masterCertificate, MessageType.GetClassLesson, Array.Empty<byte>());
        return reply.Type switch
        {
            MessageType.ClassLessonStatus when ClassLesson.TryDecode(reply.Payload, out var lesson) => (lesson, false),
            MessageType.ClassLessonStatus => throw new InvalidDataException("aula inválida"),
            MessageType.CommandAck => (null, true),
            _ => throw new InvalidDataException($"resposta inesperada ({reply.Type})"),
        };
    }

    // "This PC is the class folders' home" (two teacher PCs in one lab,
    // ClassHomeClaim), sent before a lesson starts. Not Ok, with the Agent's
    // reason, when it keeps another PC as the home (the reason starts with
    // ClassHomeClaim.OtherHomeText) or takes no take-over now; or, busy with
    // its student's folders, ClassHomeClaim.BusyText: it took nothing, as if it
    // hadn't answered (never another home). OldAgent: an
    // Agent from before two teacher PCs, which keeps no home and takes any
    // start (a bare no, which a newer Agent never answers). sending: called just
    // before the claim goes out (CommandClient.SendCommandForResult): a claim
    // whose answer is lost after that may have been taken.
    public static (bool Ok, string Detail, bool OldAgent) ClaimHome(
        KnownMachine machine, X509Certificate2 masterCertificate, ClassHomeClaim claim, int port = ConnectionClient.AgentPort,
        Action? sending = null)
    {
        var (succeeded, detail) = CommandClient.SendCommandForResult(
            machine.HostAddress, port, AgentThumbprint(machine), masterCertificate, MessageType.ClaimClassHome, claim.Encode(),
            sending: sending);
        return (succeeded, detail, !succeeded && detail.Length == 0);
    }

    // The home the machine's Agent keeps (GetClassHome): null when it has none
    // yet, or when it is an Agent from before two teacher PCs (OldAgent).
    public static (ClassHome? Home, bool OldAgent) GetHome(
        KnownMachine machine, X509Certificate2 masterCertificate, int port = ConnectionClient.AgentPort)
    {
        var reply = CommandClient.Ask(
            machine.HostAddress, port, AgentThumbprint(machine), masterCertificate, MessageType.GetClassHome, Array.Empty<byte>());
        return reply.Type switch
        {
            MessageType.ClassHomeStatus when ClassHome.TryDecode(reply.Payload, out var home) => (home, false),
            MessageType.ClassHomeStatus => throw new InvalidDataException("PC das pastas inválido"),
            MessageType.CommandAck => (null, true),
            _ => throw new InvalidDataException($"resposta inesperada ({reply.Type})"),
        };
    }

    // Does the machine's Agent trust the teacher with that certificate
    // thumbprint? True when it says so; false when it says no, which an Agent
    // from before two teacher PCs answers too (the protocol can't tell them
    // apart, and neither confirms the teacher); null when it gave no answer
    // either way (it refused the question, or answered something else).
    public static bool? IsTeacherTrusted(
        KnownMachine machine, X509Certificate2 masterCertificate, string thumbprint, int port = ConnectionClient.AgentPort)
    {
        var (trusted, detail) = CommandClient.SendCommandForResult(
            machine.HostAddress, port, AgentThumbprint(machine), masterCertificate, MessageType.IsTeacherTrusted,
            Encoding.ASCII.GetBytes(thumbprint));
        return trusted ? true : detail.Length == 0 ? false : null;
    }

    // OldSession: a Session from before the class folder (it took the request
    // for a control connection and started sending its screen). Problem: the
    // Session couldn't list (a lesson with no folder yet, "sem pasta") or
    // refused (it has no class folder); then there are no entries.
    public sealed record ListResult(
        Guid CurrentLessonId, string? Problem, IReadOnlyList<ClassFolderEntry> Entries, bool OldSession);

    // No class has this many files: a Session that keeps sending listing parts
    // past it is not followed for ever.
    public const int MaxListedFiles = 1_000_000;

    // The whole listing, however many parts it comes in (see MessageType.
    // ListClassFolder), or an exception: never part of it, which a round would
    // take for files gone from the PC. A listing that breaks off, mixes in
    // another lesson's part or has more than maxFiles files throws
    // InvalidDataException, saying so in the teacher's words.
    public static ListResult List(
        KnownMachine machine, X509Certificate2 masterCertificate, int port = ConnectionClient.SessionPort,
        int maxFiles = MaxListedFiles)
    {
        using var tcpClient = TcpConnect.WithTimeout(machine.HostAddress, port, ConnectTimeout);
        tcpClient.ReceiveTimeout = TimeoutMs;
        tcpClient.SendTimeout = TimeoutMs;
        using var channel = SecureChannel.AuthenticateAsClient(
            tcpClient, machine.HostAddress, masterCertificate, new TrustStore(new[] { machine.SessionThumbprint }));

        channel.Send(new Message(MessageType.ListClassFolder, Array.Empty<byte>()));
        var reply = channel.Receive();
        switch (reply.Type)
        {
            case MessageType.ClassFolderListing:
                return WholeListing(channel, reply, maxFiles);

            case MessageType.FrameTile:
                return new ListResult(Guid.Empty, null, Array.Empty<ClassFolderEntry>(), OldSession: true);

            case MessageType.CommandAck:
                var (_, detail) = CommandResult.Decode(reply.Payload);
                return new ListResult(
                    Guid.Empty, detail.Length > 0 ? detail : "a máquina recusou o pedido", Array.Empty<ClassFolderEntry>(), OldSession: false);

            default:
                throw new InvalidDataException($"resposta inesperada ({reply.Type})");
        }
    }

    private static ListResult WholeListing(SecureChannel channel, Message first, int maxFiles)
    {
        const string incomplete = "a lista da pasta da turma chegou incompleta";
        if (!ClassFolderWire.TryDecodeListing(first.Payload, out var current, out var problem, out var entries, out var more))
        {
            throw new InvalidDataException("lista da pasta da turma inválida");
        }

        var all = new List<ClassFolderEntry>(entries);
        while (true)
        {
            if (all.Count > maxFiles)
            {
                throw new InvalidDataException($"a pasta da turma tem arquivos demais (mais de {maxFiles})");
            }
            if (!more)
            {
                return new ListResult(current, problem, all, OldSession: false);
            }

            Message part;
            try
            {
                part = channel.Receive();
            }
            catch (IOException ex)
            {
                throw new InvalidDataException(incomplete, ex);
            }

            if (part.Type != MessageType.ClassFolderListing
                || !ClassFolderWire.TryDecodeListing(part.Payload, out var partLesson, out var partProblem, out entries, out more)
                || partLesson != current || partProblem is not null)
            {
                throw new InvalidDataException(incomplete);
            }
            all.AddRange(entries);
        }
    }

    // Writes the file to destination, which the caller owns: on anything but
    // (true, ...) it holds nothing to trust (some of the file, or none) and the
    // caller discards it. Succeeded means the machine sent exactly the size it
    // announced and said it all arrived.
    public static (bool Succeeded, string Detail) Pull(
        KnownMachine machine, X509Certificate2 masterCertificate, Guid lessonId, string path, Stream destination,
        int port = ConnectionClient.SessionPort)
    {
        byte[] request;
        try
        {
            request = ClassFolderWire.EncodeFileRef(lessonId, path);
        }
        catch (ArgumentException)
        {
            return (false, NotSendable);
        }

        using var tcpClient = TcpConnect.WithTimeout(machine.HostAddress, port, ConnectTimeout);
        tcpClient.ReceiveTimeout = TimeoutMs;
        tcpClient.SendTimeout = TimeoutMs;
        using var channel = SecureChannel.AuthenticateAsClient(
            tcpClient, machine.HostAddress, masterCertificate, new TrustStore(new[] { machine.SessionThumbprint }));

        channel.Send(new Message(MessageType.PullClassFile, request));
        var first = channel.Receive();
        switch (first.Type)
        {
            case MessageType.FrameTile:
                return (false, CommandClient.OlderSession);

            case MessageType.CommandAck:
                // A refusal; nothing else is meant to come before the header.
                var (acked, why) = CommandResult.Decode(first.Payload);
                return (false, !acked && why.Length > 0 ? why : $"resposta inesperada ({first.Type})");

            case MessageType.SendFile when FileTransfer.TryDecodeHeader(first.Payload, out _, out var size):
                return ReceiveBody(channel, size, destination);

            case MessageType.SendFile:
                return (false, "a máquina mandou um cabeçalho de arquivo inválido");

            default:
                return (false, $"resposta inesperada ({first.Type})");
        }
    }

    private static (bool Succeeded, string Detail) ReceiveBody(SecureChannel channel, long size, Stream destination)
    {
        long received = 0;
        while (received < size)
        {
            var chunk = channel.Receive();
            if (chunk.Type != MessageType.FileChunk || chunk.Payload.Length == 0)
            {
                return (false, "o arquivo chegou incompleto ou corrompido");
            }
            if (received + chunk.Payload.Length > size)
            {
                return (false, "a máquina mandou mais bytes do que o tamanho informado");
            }

            try
            {
                destination.Write(chunk.Payload);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return (false, $"não foi possível salvar o arquivo: {ErrorText.Describe(ex)}");
            }
            received += chunk.Payload.Length;
        }

        // Only the final answer may follow the last byte.
        var last = channel.Receive();
        return last.Type switch
        {
            MessageType.CommandAck => FinalAnswer(last),
            MessageType.FileChunk => (false, "a máquina mandou mais bytes do que o tamanho informado"),
            _ => (false, $"resposta inesperada ({last.Type})"),
        };
    }

    // Reads sourceFile (shared for reading only, so it can't change under the
    // transfer) and sends it, as lastWriteTicks (UTC ticks) old, to the
    // current lesson's folder on the machine. The Session says whether it
    // takes the file before a byte is sent, and whether it saved it after.
    // replacing: the version of the file at that path on the machine, as the
    // listing the push was planned on showed it, or null when it showed none;
    // the Session refuses to replace anything else ("mudou neste PC").
    public static (bool Succeeded, string Detail) Push(
        KnownMachine machine, X509Certificate2 masterCertificate, Guid lessonId, string path, string sourceFile,
        long lastWriteTicks, FileVersion? replacing, int port = ConnectionClient.SessionPort)
    {
        FileStream file;
        try
        {
            file = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, $"não foi possível ler o arquivo: {ErrorText.Describe(ex)}");
        }

        using (file)
        {
            var size = file.Length;
            if (size > FileTransfer.MaxFileSize)
            {
                return (false, "o arquivo passa de 1 GB");
            }

            byte[] header;
            try
            {
                header = ClassFolderWire.EncodePushHeader(
                    lessonId, path, size, lastWriteTicks,
                    replacing is { } version ? (version.Size, version.LastWriteTicks) : null);
            }
            catch (ArgumentException)
            {
                return (false, NotSendable);
            }

            using var tcpClient = TcpConnect.WithTimeout(machine.HostAddress, port, ConnectTimeout);
            tcpClient.ReceiveTimeout = TimeoutMs;
            tcpClient.SendTimeout = TimeoutMs;
            using var channel = SecureChannel.AuthenticateAsClient(
                tcpClient, machine.HostAddress, masterCertificate, new TrustStore(new[] { machine.SessionThumbprint }));

            channel.Send(new Message(MessageType.PushClassFile, header));
            var reply = channel.Receive();
            if (reply.Type == MessageType.FrameTile)
            {
                return (false, CommandClient.OlderSession);
            }
            var (accepted, detail) = AnswerOf(reply);
            if (!accepted)
            {
                return (false, detail);
            }

            var buffer = new byte[FileTransfer.ChunkSize];
            for (var remaining = size; remaining > 0;)
            {
                var read = file.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0)
                {
                    // Leaving now, without the last answer, is how the Session
                    // knows to throw away what it has.
                    return (false, "o arquivo diminuiu durante o envio");
                }
                channel.Send(new Message(MessageType.FileChunk, buffer.AsSpan(0, read).ToArray()));
                remaining -= read;
            }

            return AnswerOf(channel.Receive());
        }
    }

    public static (bool Succeeded, string Detail) Delete(
        KnownMachine machine, X509Certificate2 masterCertificate, Guid lessonId, string path, int port = ConnectionClient.SessionPort) =>
        Command(machine, masterCertificate, MessageType.DeleteClassFile, () => ClassFolderWire.EncodeFileRef(lessonId, path), port);

    public static (bool Succeeded, string Detail) Rename(
        KnownMachine machine, X509Certificate2 masterCertificate, Guid lessonId, string path, string newPath,
        int port = ConnectionClient.SessionPort) =>
        Command(machine, masterCertificate, MessageType.RenameClassFile, () => ClassFolderWire.EncodeRename(lessonId, path, newPath), port);

    // These files of a finished lesson were set aside on that machine and the
    // Master has dealt with them (they are in the class, or not the class's):
    // the Session sends each to the student's Recycle Bin while it is still
    // that version. At most ClassFolderWire.MaxEntries files in one call; the
    // Session takes its time over them, one Recycle Bin move each.
    public static (bool Succeeded, string Detail) ClearSetAside(
        KnownMachine machine, X509Certificate2 masterCertificate, Guid lessonId, IReadOnlyList<ClassFolderEntry> files,
        int port = ConnectionClient.SessionPort) =>
        Command(machine, masterCertificate, MessageType.ClearSetAside, () => ClassFolderWire.EncodeClearSetAside(lessonId, files),
            port, ClearingTimeout);

    private static readonly TimeSpan ClearingTimeout = TimeSpan.FromMinutes(2);

    // One request, one CommandResult; a Session from before the class folder
    // answers FrameTile, which SendCommandForResult words. A name the wire
    // can't carry never reaches the network.
    private static (bool Succeeded, string Detail) Command(
        KnownMachine machine, X509Certificate2 masterCertificate, MessageType type, Func<byte[]> payload, int port,
        TimeSpan? timeout = null)
    {
        byte[] encoded;
        try
        {
            encoded = payload();
        }
        catch (ArgumentException)
        {
            return (false, NotSendable);
        }

        return CommandClient.SendCommandForResult(
            machine.HostAddress, port, machine.SessionThumbprint, masterCertificate, type, encoded, timeout);
    }

    // A CommandAck that has to say yes or no: anything else, or a no without
    // a reason, is still a failure and still says something.
    private static (bool Succeeded, string Detail) AnswerOf(Message reply) =>
        reply.Type == MessageType.CommandAck ? FinalAnswer(reply) : (false, $"resposta inesperada ({reply.Type})");

    private static (bool Succeeded, string Detail) FinalAnswer(Message ack)
    {
        var (succeeded, detail) = CommandResult.Decode(ack.Payload);
        return !succeeded && detail.Length == 0 ? (false, "a máquina não confirmou") : (succeeded, detail);
    }

    private static string AgentThumbprint(KnownMachine machine) =>
        machine.AgentThumbprint is { Length: > 0 } thumbprint
            ? thumbprint
            : throw new InvalidOperationException("sem a identidade do Agent");
}
