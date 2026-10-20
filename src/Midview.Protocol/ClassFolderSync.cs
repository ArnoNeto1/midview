using System.Buffers.Binary;
using System.Text;

namespace Midview.Protocol;

// One file in a "Pasta da turma": its '/'-separated path inside the folder,
// its size and its last-write time (UTC ticks). Size and time together are
// the file's version: the two sides never hash anything, and every copy the
// Master makes keeps the time it came with.
public sealed record ClassFolderEntry(string Path, long Size, long LastWriteTicks)
{
    public bool SameVersionAs(ClassFolderEntry? other) =>
        other is not null && Size == other.Size && LastWriteTicks == other.LastWriteTicks;
}

// The "Pasta da turma" exchanges (see
// docs/superpowers/specs/2026-10-20-class-folder-design.md). The teacher's PC
// keeps one folder per class; the folder on each student's Desktop is a copy
// of it for the lesson.
//
// Loading (LoadClassFolder): the Master sends a ClassFolderRequest -- the
// lesson's id, the class name and every file the teacher's copy has. A
// Session holding another lesson (or none) first deletes whatever in its
// folder isn't one of those exact files; it then answers ClassFolderWanted:
// the indexes of the files it still lacks. The Master sends those it can, as
// SendFile header (the path) plus FileChunks, and closes its part with a
// CommandAck of its own; the Session answers with a ClassFolderListing of
// what its folder now holds, then one CommandAck (CommandResult).
//
// Saving (CollectClassFolder): the Master sends a ClassFolderRequest with
// what it last saw on that machine. A Session holding another lesson answers
// only CommandAck [0, NotLoaded]. Otherwise it answers a ClassFolderListing of
// everything in the folder, then each listed file that isn't exactly one the
// Master named (new or changed), as SendFile plus FileChunks, in listing
// order -- one it can't read is left out -- and ends with one CommandAck.
//
// Emptying (ClearClassFolder, no payload): everything in the folder is
// deleted and the lesson forgotten. One CommandAck (CommandResult).
public static class ClassFolderSync
{
    // A listing bigger than this is refused rather than half-read; with
    // typical paths it's well under MessageFramer.MaxMessageLength.
    public const int MaxEntries = 100_000;

    // The CommandResult detail of a CollectClassFolder answered by a Session
    // that doesn't hold that lesson: the Master loads it instead.
    public const string NotLoaded = "a pasta da turma desta aula não está nesta máquina";

    public const int MaxClassNameBytes = 1024;

    // ClassFolderRequest: lesson id (16 bytes), class name length (uint16)
    // and UTF-8 name, then a listing (EncodeListing).
    public static byte[] EncodeRequest(Guid lessonId, string className, IReadOnlyList<ClassFolderEntry> entries)
    {
        var name = Encoding.UTF8.GetBytes(className);
        if (name.Length > MaxClassNameBytes)
        {
            throw new ArgumentException("nome de turma longo demais", nameof(className));
        }
        var listing = EncodeListing(entries);
        var payload = new byte[16 + 2 + name.Length + listing.Length];
        lessonId.TryWriteBytes(payload);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(16), (ushort)name.Length);
        name.CopyTo(payload, 18);
        listing.CopyTo(payload, 18 + name.Length);
        return payload;
    }

    public static bool TryDecodeRequest(
        byte[] payload, out Guid lessonId, out string className, out IReadOnlyList<ClassFolderEntry> entries)
    {
        lessonId = Guid.Empty;
        className = string.Empty;
        entries = Array.Empty<ClassFolderEntry>();
        if (payload.Length < 18)
        {
            return false;
        }

        lessonId = new Guid(payload.AsSpan(0, 16));
        var nameLength = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(16));
        if (lessonId == Guid.Empty || nameLength > MaxClassNameBytes || payload.Length < 18 + nameLength
            || !TryDecodeText(payload.AsSpan(18, nameLength), out className))
        {
            return false;
        }
        return TryDecodeListing(payload.AsSpan(18 + nameLength), out entries);
    }

    // ClassFolderListing: entry count (int32), then per entry size (int64),
    // last-write ticks (int64), path length (uint16) and the UTF-8 path.
    public static byte[] EncodeListing(IReadOnlyList<ClassFolderEntry> entries)
    {
        if (entries.Count > MaxEntries)
        {
            throw new ArgumentException($"mais de {MaxEntries} arquivos", nameof(entries));
        }

        var paths = entries.Select(entry => Encoding.UTF8.GetBytes(entry.Path)).ToList();
        var payload = new byte[4 + paths.Sum(path => 18 + path.Length)];
        BinaryPrimitives.WriteInt32BigEndian(payload, entries.Count);
        var offset = 4;
        for (var i = 0; i < entries.Count; i++)
        {
            if (paths[i].Length == 0 || paths[i].Length > FileTransfer.MaxNameBytes)
            {
                throw new ArgumentException($"caminho inválido: {entries[i].Path}", nameof(entries));
            }
            BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(offset), entries[i].Size);
            BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(offset + 8), entries[i].LastWriteTicks);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset + 16), (ushort)paths[i].Length);
            paths[i].CopyTo(payload, offset + 18);
            offset += 18 + paths[i].Length;
        }
        return payload;
    }

    public static bool TryDecodeListing(ReadOnlySpan<byte> payload, out IReadOnlyList<ClassFolderEntry> entries)
    {
        entries = Array.Empty<ClassFolderEntry>();
        if (payload.Length < 4)
        {
            return false;
        }

        var count = BinaryPrimitives.ReadInt32BigEndian(payload);
        if (count < 0 || count > MaxEntries)
        {
            return false;
        }

        var decoded = new List<ClassFolderEntry>(Math.Min(count, 1024));
        var offset = 4;
        for (var i = 0; i < count; i++)
        {
            if (payload.Length - offset < 18)
            {
                return false;
            }
            var size = BinaryPrimitives.ReadInt64BigEndian(payload[offset..]);
            var ticks = BinaryPrimitives.ReadInt64BigEndian(payload[(offset + 8)..]);
            var pathLength = BinaryPrimitives.ReadUInt16BigEndian(payload[(offset + 16)..]);
            offset += 18;
            if (size < 0 || size > FileTransfer.MaxFileSize || !IsValidTime(ticks)
                || pathLength == 0 || pathLength > FileTransfer.MaxNameBytes || payload.Length - offset < pathLength
                || !TryDecodeText(payload.Slice(offset, pathLength), out var path) || path.Trim().Length == 0)
            {
                return false;
            }
            offset += pathLength;
            decoded.Add(new ClassFolderEntry(path, size, ticks));
        }
        if (offset != payload.Length)
        {
            return false;
        }

        entries = decoded;
        return true;
    }

    // ClassFolderWanted: count (int32), then each index (int32).
    public static byte[] EncodeWanted(IReadOnlyList<int> indexes)
    {
        var payload = new byte[4 + 4 * indexes.Count];
        BinaryPrimitives.WriteInt32BigEndian(payload, indexes.Count);
        for (var i = 0; i < indexes.Count; i++)
        {
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4 + 4 * i), indexes[i]);
        }
        return payload;
    }

    // Each index must point into a request of requestCount entries, and
    // appear once.
    public static bool TryDecodeWanted(byte[] payload, int requestCount, out IReadOnlyList<int> indexes)
    {
        indexes = Array.Empty<int>();
        if (payload.Length < 4)
        {
            return false;
        }
        var count = BinaryPrimitives.ReadInt32BigEndian(payload);
        if (count < 0 || count > requestCount || payload.Length != 4 + 4L * count)
        {
            return false;
        }

        var seen = new HashSet<int>();
        var decoded = new List<int>(count);
        for (var i = 0; i < count; i++)
        {
            var index = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(4 + 4 * i));
            if (index < 0 || index >= requestCount || !seen.Add(index))
            {
                return false;
            }
            decoded.Add(index);
        }
        indexes = decoded;
        return true;
    }

    // A time Windows can set on a file (FILETIME starts in 1601).
    public static bool IsValidTime(long ticks) =>
        ticks >= MinFileTime.Ticks && ticks <= DateTime.MaxValue.Ticks;

    private static readonly DateTime MinFileTime = new(1601, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    // The '/'-separated path a file is listed under, from its safe segments
    // (FileCollection.SafeRelativePath): both sides list and save files
    // under these, so a path always means the same file on either side.
    public static string PathOf(IReadOnlyList<string> segments) => string.Join('/', segments);

    private static bool TryDecodeText(ReadOnlySpan<byte> bytes, out string text)
    {
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }
}
