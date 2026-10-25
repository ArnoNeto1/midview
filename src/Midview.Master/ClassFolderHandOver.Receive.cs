using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderHandOver: the store on the wire, and its copy arriving on the PC
// taking the folders over. What the home sends is hostile input, as a PC's is:
// the manifest is checked whole before anything is written (each class a class
// name, each path one FileCollection.SafeRelativePath leaves as it is and that
// stays inside its class's folder in Staging, no path twice, no file where
// another needs a folder, sizes and times that can be, an index or lessons map
// no bigger than MaxDataFileBytes, and all of it fitting on the disk with
// KeepFreeBytes to spare); then each file must come in the manifest's order,
// with its path and size, and exactly that many bytes; and at the end each file
// written must have the size announced.
public sealed partial class ClassFolderHandOver
{
    internal const string ManifestPath = "manifest.json";
    internal const string LessonsWirePath = "lessons.json";

    // The most an index, the lessons map or the manifest may be: a few lines per file and PC.
    internal const long MaxDataFileBytes = 64L * 1024 * 1024;

    private const string FilesPrefix = "files/";
    private const string IndexPrefix = "index/";
    private const string IndexSuffix = ".json";

    // A path inside a class (at most FileTransfer.MaxNameBytes) with "files/<turma>/" before it.
    private const int MaxWirePathBytes = FileTransfer.MaxNameBytes + 512;

    private const int MaxClasses = 10_000;

    private const int MaxFiles = ClassFolderClient.MaxListedFiles;

    // How often the copy's progress is told, at most (and after the first file and the last).
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(200);

    private static readonly long FirstFileTimeTicks = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks + 1;

    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 8,
    };

    internal static string FileWirePath(string turma, string path) => FilesPrefix + turma + "/" + path;

    internal static string IndexWirePath(string turma) => IndexPrefix + turma + IndexSuffix;

    internal static byte[] EncodeManifest(
        IReadOnlyList<string> classes, IReadOnlyList<(string Path, long Size, long LastWriteTicks)> files) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new Manifest(classes, files.Select(file => new ManifestFile(file.Path, file.Size, file.LastWriteTicks)).ToList()),
            ManifestOptions);

    // The home's store into Staging (which the caller discards on a failure).
    private Received Receive(ClassHome home, string name, IProgress<HandOverProgress>? progress)
    {
        System.Net.Sockets.TcpClient tcp;
        try
        {
            tcp = TcpConnect.WithTimeout(home.Address, home.Port, ConnectTimeout);
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or OperationCanceledException)
        {
            return Received.Failed(ClassFolderText.HomeNoAnswer(name));
        }

        using (tcp)
        {
            SecureChannel channel;
            try
            {
                tcp.ReceiveTimeout = tcp.SendTimeout = (int)NetworkWait.TotalMilliseconds;
                channel = SecureChannel.AuthenticateAsClientAcceptingAnyServer(tcp, home.Address, _certificate);
            }
            catch (Exception ex) when (IsNetwork(ex))
            {
                return Received.Failed(ClassFolderText.HomeNoAnswer(name));
            }

            using (channel)
            {
                if (!string.Equals(channel.RemoteThumbprint, home.Thumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    return Received.Failed(ClassFolderText.HomeOtherIdentity(name));
                }
                try
                {
                    channel.Send(new Message(MessageType.RemoteHandOver, Array.Empty<byte>()));
                    CommandClient.EnableKeepAlive(tcp.Client);
                    return new Copy(this, channel, name, progress).Run();
                }
                catch (InvalidDataException)
                {
                    return Received.Failed(ClassFolderText.CopyMismatch(name)); // a message no Midview sends
                }
                catch (Exception ex) when (IsNetwork(ex))
                {
                    _log($"The copy of the class folders from {name} broke off: {ex.Message}");
                    return Received.Failed(ClassFolderText.CopyDropped(name));
                }
            }
        }
    }

    // The manifest's files, each with where it goes in staging, or null when anything in it can't be (see the class comment).
    private static List<(ManifestFile File, string Target)>? Plan(Manifest? manifest, HandOverStaging staging)
    {
        if (manifest?.Classes is not { Count: <= MaxClasses } classList || manifest.Files is not { Count: <= MaxFiles } fileList)
        {
            return null;
        }

        var classes = new HashSet<string>(StringComparer.Ordinal);
        var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var turma in classList)
        {
            if (turma is null || !ClassStore.IsValidName(turma) || !distinct.Add(turma))
            {
                return null;
            }
            classes.Add(turma);
        }

        var plan = new List<(ManifestFile File, string Target)>();
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in fileList)
        {
            if (file?.Path is not { } path || file.Size is < 0 or > FileTransfer.MaxFileSize
                || file.LastWriteTicks < FirstFileTimeTicks || file.LastWriteTicks > DateTime.MaxValue.Ticks
                || TargetOf(path, staging, classes, out var isData) is not { } target
                || (isData && file.Size > MaxDataFileBytes) || !targets.Add(target))
            {
                return null;
            }
            plan.Add((file, target));
        }

        // A file where another one needs a folder ("a.txt" and "a.txt/b.txt").
        foreach (var (_, target) in plan)
        {
            for (var folder = Path.GetDirectoryName(target); folder is not null && ClassFolderDisk.IsInside(staging.Files, folder);
                folder = Path.GetDirectoryName(folder))
            {
                if (targets.Contains(folder))
                {
                    return null;
                }
            }
        }
        return plan;
    }

    // Where the path on the wire goes in staging; null for one that can't be.
    // isData: an index or the lessons map rather than a class's file.
    private static string? TargetOf(string path, HandOverStaging staging, HashSet<string> classes, out bool isData)
    {
        isData = true;
        if (path == LessonsWirePath)
        {
            return staging.Lessons;
        }
        if (path.Length > IndexPrefix.Length + IndexSuffix.Length
            && path.StartsWith(IndexPrefix, StringComparison.Ordinal) && path.EndsWith(IndexSuffix, StringComparison.Ordinal))
        {
            var indexed = path[IndexPrefix.Length..^IndexSuffix.Length];
            return classes.Contains(indexed) ? Path.Combine(staging.Index, indexed + IndexSuffix) : null;
        }

        isData = false;
        if (!path.StartsWith(FilesPrefix, StringComparison.Ordinal))
        {
            return null;
        }
        var rest = path[FilesPrefix.Length..];
        var cut = rest.IndexOf('/');
        if (cut <= 0)
        {
            return null;
        }
        var turma = rest[..cut];
        var relative = rest[(cut + 1)..];
        if (!classes.Contains(turma) || !ClassFolderRules.FitsTheWire(relative)
            || !string.Equals(string.Join('/', FileCollection.SafeRelativePath(relative)), relative, StringComparison.Ordinal))
        {
            return null;
        }
        var target = ClassFolderDisk.PathInside(Path.Combine(staging.Files, turma), relative);
        return target is not null && ClassFolderDisk.IsInside(staging.Files, target) ? target : null;
    }

    private static Manifest? DecodeManifest(byte[] payload)
    {
        try
        {
            return JsonSerializer.Deserialize<Manifest>(payload, ManifestOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    // A SendFile header with a path as long as the wire's (FileTransfer.TryDecodeHeader allows a name only).
    private static bool TryHeader(Message message, out string path, out long size)
    {
        path = string.Empty;
        size = 0;
        var payload = message.Payload;
        if (message.Type != MessageType.SendFile || payload.Length <= 8 || payload.Length > 8 + MaxWirePathBytes)
        {
            return false;
        }
        size = BinaryPrimitives.ReadInt64BigEndian(payload);
        try
        {
            path = StrictUtf8.GetString(payload, 8, payload.Length - 8);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        return size is >= 0 and <= FileTransfer.MaxFileSize;
    }

    private sealed record Manifest(IReadOnlyList<string> Classes, IReadOnlyList<ManifestFile> Files);

    private sealed record ManifestFile(string Path, long Size, long LastWriteTicks);

    // A class's file, not an index or the lessons map: what the teacher counts.
    private static bool IsClassFile(ManifestFile file) => file.Path.StartsWith(FilesPrefix, StringComparison.Ordinal);

    // What arrived: the classes and their files (not counting indexes and the
    // lessons map), or why it didn't, in the teacher's words.
    private sealed record Received(string? Failure, int Classes, int Files)
    {
        public static Received Failed(string why) => new(why, 0, 0);
    }

    // One copy arriving over channel (see the class comment). Network exceptions go up to Receive.
    private sealed class Copy
    {
        private readonly ClassFolderHandOver _owner;
        private readonly SecureChannel _channel;
        private readonly string _name;
        private readonly IProgress<HandOverProgress>? _progress;
        private int _files;
        private int _totalFiles;
        private long _bytes;
        private long _totalBytes;
        private long _toldAt;

        public Copy(ClassFolderHandOver owner, SecureChannel channel, string name, IProgress<HandOverProgress>? progress)
        {
            _owner = owner;
            _channel = channel;
            _name = name;
            _progress = progress;
        }

        private string Mismatch => ClassFolderText.CopyMismatch(_name);

        public Received Run()
        {
            var first = _channel.Receive();
            if (Refusal(first) is { } refused)
            {
                return Received.Failed(refused);
            }
            if (!TryHeader(first, out var path, out var size) || path != ManifestPath || size > MaxDataFileBytes)
            {
                return Received.Failed(Mismatch);
            }
            using var manifestBytes = new MemoryStream();
            if (Body(size, manifestBytes, counted: false) is { } broken)
            {
                return Received.Failed(broken);
            }

            var manifest = DecodeManifest(manifestBytes.ToArray());
            var staging = _owner._store.Staging;
            if (Plan(manifest, staging) is not { } plan || manifest is null)
            {
                return Received.Failed(Mismatch);
            }
            _totalFiles = plan.Count(entry => IsClassFile(entry.File));
            _totalBytes = plan.Sum(entry => entry.File.Size);
            var free = _owner._store.FreeBytes();
            if (free != long.MaxValue && _totalBytes > free - KeepFreeBytes)
            {
                return Received.Failed(ClassFolderText.CopyNoRoom(_totalBytes, free));
            }

            try
            {
                Directory.CreateDirectory(staging.Index);
                foreach (var turma in manifest.Classes)
                {
                    Directory.CreateDirectory(Path.Combine(staging.Files, turma));
                }
                Directory.CreateDirectory(staging.Files); // with no class at all, still a store
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Received.Failed(ClassFolderText.CopyNotSaved(ErrorText.Describe(ex)));
            }
            Tell(force: true);

            foreach (var (file, target) in plan)
            {
                if (One(file, target) is { } failed)
                {
                    return Received.Failed(failed);
                }
            }

            var last = _channel.Receive();
            if (last.Type != MessageType.CommandAck)
            {
                return Received.Failed(Mismatch);
            }
            var (done, why) = CommandResult.Decode(last.Payload);
            if (!done)
            {
                return Received.Failed(why.Length > 0 ? why : ClassFolderText.HomeNoAnswer(_name));
            }

            // What is on the disk now is what was announced, file by file.
            if (plan.Any(entry => !File.Exists(entry.Target) || new FileInfo(entry.Target).Length != entry.File.Size))
            {
                return Received.Failed(Mismatch);
            }
            return new Received(null, manifest.Classes.Count, _totalFiles);
        }

        // One file of the manifest: its header, then its bytes into target, with its time.
        private string? One(ManifestFile file, string target)
        {
            var header = _channel.Receive();
            if (Refusal(header) is { } refused)
            {
                return refused;
            }
            if (!TryHeader(header, out var path, out var size) || path != file.Path || size != file.Size)
            {
                return Mismatch;
            }

            FileStream stream;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return ClassFolderText.CopyNotSaved(ErrorText.Describe(ex));
            }

            using (stream)
            {
                if (Body(size, stream, counted: true) is { } broken)
                {
                    return broken;
                }
            }
            try
            {
                File.SetLastWriteTimeUtc(target, new DateTime(file.LastWriteTicks, DateTimeKind.Utc));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return ClassFolderText.CopyNotSaved(ErrorText.Describe(ex));
            }
            if (IsClassFile(file))
            {
                _files++;
            }
            Tell(force: _files <= 1 || _files == _totalFiles);
            return null;
        }

        // Exactly size bytes in FileChunks, into destination. counted: they count toward the progress.
        private string? Body(long size, Stream destination, bool counted)
        {
            long received = 0;
            while (received < size)
            {
                var chunk = _channel.Receive();
                if (Refusal(chunk) is { } refused)
                {
                    return refused;
                }
                if (chunk.Type != MessageType.FileChunk || chunk.Payload.Length == 0 || received + chunk.Payload.Length > size)
                {
                    return Mismatch;
                }
                try
                {
                    destination.Write(chunk.Payload);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return ClassFolderText.CopyNotSaved(ErrorText.Describe(ex));
                }
                received += chunk.Payload.Length;
                if (counted)
                {
                    _bytes += chunk.Payload.Length;
                    Tell(force: false);
                }
            }
            return null;
        }

        // A CommandAck before the end: why the rest won't come (the home's own words, when it gave any).
        private string? Refusal(Message message)
        {
            if (message.Type != MessageType.CommandAck)
            {
                return null;
            }
            var (ok, why) = CommandResult.Decode(message.Payload);
            return !ok && why.Length > 0 ? why : ok ? Mismatch : ClassFolderText.HomeNoAnswer(_name);
        }

        private void Tell(bool force)
        {
            if (_progress is null || (!force && Environment.TickCount64 - _toldAt < ProgressInterval.TotalMilliseconds))
            {
                return;
            }
            _toldAt = Environment.TickCount64;
            _progress.Report(new HandOverProgress(_files, _totalFiles, _bytes, _totalBytes));
        }
    }
}
