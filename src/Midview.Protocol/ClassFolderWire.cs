using System.Buffers.Binary;
using System.Text;

namespace Midview.Protocol;

// One file the Session found for the class folder. LessonId equal to the
// listing's current lesson means the file is in the Desktop folder; any other
// LessonId means it was set aside for that lesson. Path is relative to that
// folder, '/'-separated.
public sealed record ClassFolderEntry(Guid LessonId, string Path, long Size, long LastWriteTicks);

// Payloads of the class folder's Session messages (see MessageType.
// ListClassFolder and the ones after it, and docs/superpowers/specs/
// 2026-10-20-class-folder-design.md), all big-endian and each starting with a
// format version byte. A string is a uint16 byte length, then UTF-8, and a
// path is at most FileTransfer.MaxNameBytes of it. Decoding is strict: a
// payload with a bad version, a bad string, a size or time out of range, or
// bytes left over is refused as a whole. Encoding refuses what decoding would,
// so a caller leaves out what doesn't fit (a file over FileTransfer.
// MaxFileSize, say) instead of sending a payload the other side drops.
public static partial class ClassFolderWire
{
    public const byte FormatVersion = 1;

    // Entries in one ClassFolderListing message. A folder with more is listed
    // in several (EncodeListingParts), so there is no limit on the files.
    public const int MaxEntries = 10_000;

    // A problem is a sentence for the teacher; only the length prefix limits it.
    private const int MaxProblemBytes = ushort.MaxValue;

    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    // ClassFolderListing payload: version, the current lesson (16 bytes), one
    // byte that is 1 when a problem follows (a string: the Session couldn't
    // list, e.g. "sem pasta", and there are then no entries), one byte that is
    // 1 when more ClassFolderListing messages follow with the rest of the
    // entries (never with a problem), the number of entries (uint32, at most
    // MaxEntries), then each entry: lesson (16 bytes), path (a string), size
    // (int64), last write time in DateTime ticks (int64).
    public static byte[] EncodeListing(
        Guid currentLessonId, string? problem, IReadOnlyList<ClassFolderEntry> entries, bool more = false)
    {
        if (problem is not null && entries.Count > 0)
        {
            throw new ArgumentException("A listing with a problem has no entries.", nameof(entries));
        }

        if (problem is not null && more)
        {
            throw new ArgumentException("A listing with a problem is the whole listing.", nameof(more));
        }

        if (entries.Count > MaxEntries)
        {
            throw new ArgumentException($"A listing has at most {MaxEntries} entries.", nameof(entries));
        }

        var writer = new Writer();
        writer.PutByte(FormatVersion);
        writer.PutGuid(currentLessonId);
        if (problem is null)
        {
            writer.PutByte(0);
        }
        else
        {
            writer.PutByte(1);
            writer.PutText(problem, MaxProblemBytes);
        }

        writer.PutByte(more ? (byte)1 : (byte)0);
        writer.PutUInt32((uint)entries.Count);
        foreach (var entry in entries)
        {
            writer.PutGuid(entry.LessonId);
            writer.PutPath(entry.Path);
            writer.PutInt64(CheckSize(entry.Size, nameof(entries)));
            writer.PutInt64(CheckTicks(entry.LastWriteTicks, nameof(entries)));
        }
        return writer.ToArray();
    }

    // A whole listing, however many files, as the ClassFolderListing messages
    // to send one after the other: entries in order, at most partSize in each,
    // every one but the last saying more follow. Nothing to list, or a
    // problem, is one message.
    public static IEnumerable<byte[]> EncodeListingParts(
        Guid currentLessonId, string? problem, IReadOnlyList<ClassFolderEntry> entries, int partSize = MaxEntries)
    {
        if (partSize < 1 || partSize > MaxEntries)
        {
            throw new ArgumentOutOfRangeException(nameof(partSize), partSize, $"A part has 1 to {MaxEntries} entries.");
        }

        return Parts();

        IEnumerable<byte[]> Parts()
        {
            var start = 0;
            do
            {
                var count = Math.Min(partSize, entries.Count - start);
                var part = new ClassFolderEntry[count];
                for (var i = 0; i < count; i++)
                {
                    part[i] = entries[start + i];
                }
                start += count;
                yield return EncodeListing(currentLessonId, problem, part, more: start < entries.Count);
            }
            while (start < entries.Count);
        }
    }

    public static bool TryDecodeListing(
        byte[] payload, out Guid currentLessonId, out string? problem, out IReadOnlyList<ClassFolderEntry> entries,
        out bool more)
    {
        currentLessonId = Guid.Empty;
        problem = null;
        entries = Array.Empty<ClassFolderEntry>();
        more = false;

        var reader = new Reader(payload);
        if (!reader.TryVersion() || !reader.TryGuid(out var current) || !reader.TryByte(out var hasProblem) || hasProblem > 1)
        {
            return false;
        }

        string? decodedProblem = null;
        if (hasProblem == 1)
        {
            if (!reader.TryText(MaxProblemBytes, out var text))
            {
                return false;
            }
            decodedProblem = text;
        }

        if (!reader.TryByte(out var moreFollow) || moreFollow > 1 || (decodedProblem is not null && moreFollow == 1)
            || !reader.TryUInt32(out var count) || count > MaxEntries || (decodedProblem is not null && count > 0))
        {
            return false;
        }

        var decoded = new List<ClassFolderEntry>((int)count);
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryGuid(out var lessonId) || !reader.TryPath(out var path)
                || !reader.TryInt64(out var size) || !IsSize(size)
                || !reader.TryInt64(out var ticks) || !IsTicks(ticks))
            {
                return false;
            }
            decoded.Add(new ClassFolderEntry(lessonId, path, size, ticks));
        }

        if (!reader.AtEnd)
        {
            return false;
        }

        currentLessonId = current;
        problem = decodedProblem;
        entries = decoded;
        more = moreFollow == 1;
        return true;
    }

    // PullClassFile and DeleteClassFile payload: version, lesson (16 bytes),
    // path (a string).
    public static byte[] EncodeFileRef(Guid lessonId, string path)
    {
        var writer = new Writer();
        writer.PutByte(FormatVersion);
        writer.PutGuid(lessonId);
        writer.PutPath(path);
        return writer.ToArray();
    }

    public static bool TryDecodeFileRef(byte[] payload, out Guid lessonId, out string path)
    {
        lessonId = Guid.Empty;
        path = "";

        var reader = new Reader(payload);
        if (!reader.TryVersion() || !reader.TryGuid(out var decodedLesson)
            || !reader.TryPath(out var decodedPath) || !reader.AtEnd)
        {
            return false;
        }

        lessonId = decodedLesson;
        path = decodedPath;
        return true;
    }

    // RenameClassFile payload: version, lesson (16 bytes), path, new path
    // (both strings).
    public static byte[] EncodeRename(Guid lessonId, string path, string newPath)
    {
        var writer = new Writer();
        writer.PutByte(FormatVersion);
        writer.PutGuid(lessonId);
        writer.PutPath(path);
        writer.PutPath(newPath);
        return writer.ToArray();
    }

    public static bool TryDecodeRename(byte[] payload, out Guid lessonId, out string path, out string newPath)
    {
        lessonId = Guid.Empty;
        path = "";
        newPath = "";

        var reader = new Reader(payload);
        if (!reader.TryVersion() || !reader.TryGuid(out var decodedLesson)
            || !reader.TryPath(out var decodedPath) || !reader.TryPath(out var decodedNewPath) || !reader.AtEnd)
        {
            return false;
        }

        lessonId = decodedLesson;
        path = decodedPath;
        newPath = decodedNewPath;
        return true;
    }

    // PushClassFile payload, ahead of the file's FileChunk messages: version,
    // lesson (16 bytes), path (a string), size (int64), last write time in
    // DateTime ticks (int64), which the Session gives the file it writes.
    public static byte[] EncodePushHeader(Guid lessonId, string path, long size, long lastWriteTicks)
    {
        var writer = new Writer();
        writer.PutByte(FormatVersion);
        writer.PutGuid(lessonId);
        writer.PutPath(path);
        writer.PutInt64(CheckSize(size, nameof(size)));
        writer.PutInt64(CheckTicks(lastWriteTicks, nameof(lastWriteTicks)));
        return writer.ToArray();
    }

    public static bool TryDecodePushHeader(
        byte[] payload, out Guid lessonId, out string path, out long size, out long lastWriteTicks)
    {
        lessonId = Guid.Empty;
        path = "";
        size = 0;
        lastWriteTicks = 0;

        var reader = new Reader(payload);
        if (!reader.TryVersion() || !reader.TryGuid(out var decodedLesson) || !reader.TryPath(out var decodedPath)
            || !reader.TryInt64(out var decodedSize) || !IsSize(decodedSize)
            || !reader.TryInt64(out var decodedTicks) || !IsTicks(decodedTicks)
            || !reader.AtEnd)
        {
            return false;
        }

        lessonId = decodedLesson;
        path = decodedPath;
        size = decodedSize;
        lastWriteTicks = decodedTicks;
        return true;
    }

    // ClearSetAside payload: version, lesson (16 bytes).
    public static byte[] EncodeLessonId(Guid lessonId)
    {
        var writer = new Writer();
        writer.PutByte(FormatVersion);
        writer.PutGuid(lessonId);
        return writer.ToArray();
    }

    public static bool TryDecodeLessonId(byte[] payload, out Guid lessonId)
    {
        lessonId = Guid.Empty;

        var reader = new Reader(payload);
        if (!reader.TryVersion() || !reader.TryGuid(out var decodedLesson) || !reader.AtEnd)
        {
            return false;
        }

        lessonId = decodedLesson;
        return true;
    }

    private static bool IsSize(long size) => size >= 0 && size <= FileTransfer.MaxFileSize;

    // What a DateTime can hold, so the receiver can build one from it.
    private static bool IsTicks(long ticks) => ticks >= 0 && ticks <= DateTime.MaxValue.Ticks;

    private static long CheckSize(long size, string parameter) =>
        IsSize(size)
            ? size
            : throw new ArgumentOutOfRangeException(parameter, size, $"A file size is 0 to {FileTransfer.MaxFileSize}.");

    private static long CheckTicks(long ticks, string parameter) =>
        IsTicks(ticks)
            ? ticks
            : throw new ArgumentOutOfRangeException(parameter, ticks, "A last write time is 0 to DateTime.MaxValue ticks.");
}
