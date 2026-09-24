using System.IO;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// Succeeded: the collection finished (Detail lists what the machine left
// out, if anything). Otherwise Detail says why it stopped; the Files already
// saved by then stay.
public sealed record CollectResult(bool Succeeded, int Files, long Bytes, string Detail);

// Collects one machine's "Entregar ao professor" folder into
// destinationDirectory (see FileCollection). Whatever the machine sends is
// treated as hostile -- a student could stand in for their own machine's
// Session: every path is rebuilt from sanitized segments and checked to stay
// inside the destination, nothing is ever overwritten, the limits are
// enforced here too, and a file only appears under its name once all of it
// has arrived. Nothing received is opened or run.
public static class FileCollector
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    // Per read or write, like FileSender.
    private const int TimeoutMs = 30_000;

    public static CollectResult Collect(
        KnownMachine machine, X509Certificate2 masterCertificate, string destinationDirectory,
        int port = ConnectionClient.SessionPort, int maxFiles = FileCollection.MaxFiles,
        long maxTotalBytes = FileCollection.MaxTotalBytes)
    {
        using var tcpClient = TcpConnect.WithTimeout(machine.HostAddress, port, ConnectTimeout);
        tcpClient.ReceiveTimeout = TimeoutMs;
        tcpClient.SendTimeout = TimeoutMs;
        using var channel = SecureChannel.AuthenticateAsClient(
            tcpClient, machine.HostAddress, masterCertificate, new TrustStore(new[] { machine.SessionThumbprint }));

        channel.Send(new Message(MessageType.CollectFiles, Array.Empty<byte>()));

        var root = Path.GetFullPath(destinationDirectory);
        var received = 0; // the limits count every file sent, saved or not
        long receivedBytes = 0;
        var saved = 0;
        long savedBytes = 0;
        var notSaved = new List<string>();
        try
        {
            while (true)
            {
                var message = channel.Receive();
                if (message.Type == MessageType.CommandAck)
                {
                    var (succeeded, detail) = CommandResult.Decode(message.Payload);
                    return new CollectResult(succeeded, saved, savedBytes, Join(detail, notSaved));
                }
                if (message.Type != MessageType.SendFile
                    || !FileTransfer.TryDecodeHeader(message.Payload, out var relativePath, out var size))
                {
                    throw new InvalidDataException($"resposta inesperada ({message.Type})");
                }
                if (received == maxFiles)
                {
                    throw new InvalidDataException($"a máquina mandou mais de {maxFiles} arquivos");
                }
                if (receivedBytes + size > maxTotalBytes)
                {
                    throw new InvalidDataException($"a máquina mandou mais de {maxTotalBytes / (1024 * 1024)} MB");
                }

                received++;
                receivedBytes += size;
                var problem = ReceiveFile(channel, root, relativePath, size);
                if (problem is null)
                {
                    saved++;
                    savedBytes += size;
                }
                else
                {
                    notSaved.Add(problem);
                }
            }
        }
        catch (InvalidDataException ex)
        {
            return new CollectResult(false, saved, savedBytes, Join(ex.Message, notSaved));
        }
        catch (IOException ex)
        {
            // The connection dropped (the file being received is discarded).
            return new CollectResult(false, saved, savedBytes, Join($"a conexão caiu: {FailureText.Describe(ex)}", notSaved));
        }
    }

    // Null once the file is saved. A file this PC can't save where it
    // belongs (a path too long for Windows, say) is still read off the
    // connection, so the rest of the collection goes on, and why is
    // returned. Anything that would affect every file after it (a full disk,
    // data that breaks the protocol) stops the collection instead.
    private static string? ReceiveFile(SecureChannel channel, string root, string relativePath, long size)
    {
        var segments = FileCollection.SafeRelativePath(relativePath);
        var directory = Path.GetFullPath(Path.Combine(new[] { root }.Concat(segments.Take(segments.Count - 1)).ToArray()));
        if (!IsInside(directory, root))
        {
            throw new InvalidDataException($"caminho inválido: {relativePath}");
        }
        var free = OnDisk(relativePath, () => new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace);
        if (free < size)
        {
            throw new InvalidDataException("não há espaço livre suficiente neste computador");
        }

        var tempPath = Path.Combine(directory, $".midview-{Guid.NewGuid():N}.parcial");
        FileStream? stream = null;
        string? problem = null;
        try
        {
            Directory.CreateDirectory(directory);
            stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            problem = $"{relativePath} não pôde ser salvo ({ex.Message})";
        }

        try
        {
            using (stream)
            {
                long receivedSoFar = 0;
                while (receivedSoFar < size)
                {
                    var chunk = channel.Receive();
                    if (chunk.Type != MessageType.FileChunk || chunk.Payload.Length == 0
                        || receivedSoFar + chunk.Payload.Length > size)
                    {
                        throw new InvalidDataException($"{relativePath} chegou incompleto ou corrompido");
                    }
                    if (stream is not null)
                    {
                        OnDisk(relativePath, () => stream.Write(chunk.Payload));
                    }
                    receivedSoFar += chunk.Payload.Length;
                }
                if (stream is not null)
                {
                    OnDisk(relativePath, () => stream.Flush(flushToDisk: true));
                }
            }
            if (stream is not null)
            {
                OnDisk(relativePath, () => FileNames.MoveToUniqueName(tempPath, directory, segments[^1]));
            }
            return problem;
        }
        finally
        {
            TryDelete(tempPath); // already moved away when the file completed
        }
    }

    private static string Join(string detail, List<string> notSaved)
    {
        var parts = new List<string>();
        if (detail.Length > 0)
        {
            parts.Add(detail);
        }
        if (notSaved.Count > 0)
        {
            parts.Add($"{notSaved.Count} não salvo(s) aqui: {string.Join(", ", notSaved.Take(3))}{(notSaved.Count > 3 ? $" e mais {notSaved.Count - 3}" : string.Empty)}");
        }
        return string.Join("; ", parts);
    }

    // A failure writing here (full disk, a path Windows can't take) stops
    // the collection with a clear reason; network failures stay IOExceptions.
    private static T OnDisk<T>(string relativePath, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException($"não foi possível salvar {relativePath}: {ex.Message}", ex);
        }
    }

    private static void OnDisk(string relativePath, Action action) =>
        OnDisk(relativePath, () =>
        {
            action();
            return true;
        });

    private static bool IsInside(string path, string root) =>
        string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
