using Midview.Protocol;

namespace Midview.Session;

// SessionServer: the class folder ("Pasta da turma") the teacher's Master lists,
// reads and writes through the Session (see ClassFolder).
public sealed partial class SessionServer
{
    // Without a class folder every message is refused in one CommandAck, the
    // listing's too: the Master takes any reply but a ClassFolderListing as a
    // failure. Reading or writing a file never tells the student: the folder
    // is theirs to look at, and the files show up in it.
    private void ServeClassFolder(SecureChannel channel, Midview.Protocol.Message message)
    {
        if (_classFolder is null)
        {
            SendResult(channel, false, "esta máquina não tem pasta da turma");
            return;
        }

        switch (message.Type)
        {
            case MessageType.ListClassFolder:
                // All of it, in as many messages as it takes, the last saying so.
                var (currentLessonId, problem, entries) = _classFolder.List();
                foreach (var part in ClassFolderWire.EncodeListingParts(
                    currentLessonId, problem, entries, _classFolder.ListingPartSize))
                {
                    channel.Send(new Midview.Protocol.Message(MessageType.ClassFolderListing, part));
                }
                break;

            case MessageType.PullClassFile:
                SendClassFile(channel, message.Payload);
                break;

            case MessageType.PushClassFile:
                ReceiveClassFile(channel, message.Payload);
                break;

            default:
                var (ok, detail) = ChangeClassFolder(message);
                SendResult(channel, ok, detail);
                break;
        }
    }

    // Delete, rename and clear: a payload that doesn't decode is refused, never guessed at.
    private (bool Ok, string Detail) ChangeClassFolder(Midview.Protocol.Message message)
    {
        const string invalid = "pedido inválido";
        switch (message.Type)
        {
            case MessageType.DeleteClassFile:
                return ClassFolderWire.TryDecodeFileRef(message.Payload, out var lessonId, out var path)
                    ? _classFolder!.Delete(lessonId, path)
                    : (false, invalid);

            case MessageType.RenameClassFile:
                return ClassFolderWire.TryDecodeRename(message.Payload, out var renamedLesson, out var oldPath, out var newPath)
                    ? _classFolder!.Rename(renamedLesson, oldPath, newPath)
                    : (false, invalid);

            default:
                return ClassFolderWire.TryDecodeClearSetAside(message.Payload, out var finishedLesson, out var handled)
                    ? _classFolder!.ClearSetAside(finishedLesson, handled)
                    : (false, invalid);
        }
    }

    // The file as a SendFile header and FileChunks, then a CommandAck saying
    // it all arrived; or one CommandAck alone for a file that can't be found
    // or opened. As with the delivery folder, a file that fails midway can't
    // be taken back, so the connection just drops and the Master discards it --
    // and so does one whose length changed while it went out, which the Master
    // would otherwise take for the whole file.
    private void SendClassFile(SecureChannel channel, byte[] payload)
    {
        if (!ClassFolderWire.TryDecodeFileRef(payload, out var lessonId, out var path))
        {
            SendResult(channel, false, "pedido inválido");
            return;
        }

        var fullPath = _classFolder!.ResolveExisting(lessonId, path);
        if (fullPath is null)
        {
            SendResult(channel, false, "arquivo não encontrado");
            return;
        }

        FileStream stream;
        try
        {
            stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SendResult(channel, false, $"não foi possível ler o arquivo: {ErrorText.Describe(ex)}");
            return;
        }

        using (stream)
        {
            var size = stream.Length;
            if (size > FileTransfer.MaxFileSize)
            {
                SendResult(channel, false, "arquivo grande demais");
                return;
            }

            channel.Send(new Midview.Protocol.Message(MessageType.SendFile, FileTransfer.EncodeHeader(path, size)));
            var buffer = new byte[FileTransfer.ChunkSize];
            for (var remaining = size; remaining > 0;)
            {
                var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0)
                {
                    throw new IOException($"{path} diminuiu durante o envio");
                }
                channel.Send(new Midview.Protocol.Message(MessageType.FileChunk, buffer.AsSpan(0, read).ToArray()));
                remaining -= read;
            }

            if (stream.Length != size)
            {
                throw new IOException($"{path} mudou de tamanho durante o envio");
            }
        }
        SendResult(channel, true, string.Empty);
    }

    // Accepted or refused up front (a first CommandAck), before the Master
    // streams anything, as ReceiveFile does; a second CommandAck reports how
    // saving it ended. Only the lesson that is on takes files (BeginPush).
    private void ReceiveClassFile(SecureChannel channel, byte[] header)
    {
        if (!ClassFolderWire.TryDecodePushHeader(header, out var lessonId, out var path, out var size, out var lastWriteTicks,
            out var replacing))
        {
            SendResult(channel, false, "pedido de envio inválido");
            return;
        }

        ClassFolderIncoming? incoming = null;
        string? refusal;
        try
        {
            incoming = _classFolder!.BeginPush(lessonId, path, replacing);
            refusal = _classFolder.RefusalFor(size);
            if (refusal is not null)
            {
                incoming.Dispose();
            }
        }
        catch (InvalidOperationException ex)
        {
            refusal = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            refusal = $"não foi possível salvar o arquivo: {ErrorText.Describe(ex)}";
        }

        if (refusal is not null)
        {
            SendResult(channel, false, refusal);
            return;
        }

        // Disposed before the last answer goes out: whatever wasn't saved is
        // gone by the time the Master hears so.
        (bool Saved, string Detail) outcome;
        using (incoming)
        {
            SendResult(channel, true, string.Empty);
            outcome = SaveClassFile(channel, incoming!, size, lastWriteTicks);
        }
        SendResult(channel, outcome.Saved, outcome.Detail);
    }

    private static (bool Saved, string Detail) SaveClassFile(
        SecureChannel channel, ClassFolderIncoming incoming, long size, long lastWriteTicks)
    {
        try
        {
            long received = 0;
            while (received < size)
            {
                var chunk = channel.Receive();
                if (chunk.Type != MessageType.FileChunk || chunk.Payload.Length == 0
                    || received + chunk.Payload.Length > size)
                {
                    return (false, "a transferência chegou incompleta ou corrompida");
                }
                incoming.Write(chunk.Payload);
                received += chunk.Payload.Length;
            }

            try
            {
                incoming.Complete(lastWriteTicks);
            }
            catch (ArgumentOutOfRangeException)
            {
                // A time the wire format can say and Windows can't store (before 1601).
                return (false, "não foi possível salvar o arquivo: a data do arquivo não pode ser guardada");
            }
            catch (InvalidOperationException ex)
            {
                // The file there isn't what the Master expected to replace ("mudou neste PC").
                return (false, ex.Message);
            }
            return (true, string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, $"não foi possível salvar o arquivo: {ErrorText.Describe(ex)}");
        }
    }

    private static void SendResult(SecureChannel channel, bool ok, string detail) =>
        channel.Send(new Midview.Protocol.Message(MessageType.CommandAck, CommandResult.Encode(ok, detail)));
}
