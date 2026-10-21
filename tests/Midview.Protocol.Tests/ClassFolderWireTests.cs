using System.Buffers.Binary;
using System.Text;
using Midview.Protocol;
using Xunit;

public class ClassFolderWireTests
{
    private static readonly Guid Lesson = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");
    private static readonly Guid Earlier = Guid.Parse("a1b2c3d4-0000-4000-8000-0123456789ab");
    private const string MapPath = "fotos/viagem/mapa.png";

    // The wire format written out by hand, so a change to it fails here.
    // Strings: uint16 big-endian byte length, then UTF-8.
    private static void PutString(List<byte> bytes, byte[] text, ushort? declaredLength = null)
    {
        var length = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, declaredLength ?? (ushort)text.Length);
        bytes.AddRange(length);
        bytes.AddRange(text);
    }

    private static void PutString(List<byte> bytes, string text) => PutString(bytes, Encoding.UTF8.GetBytes(text));

    private static void PutInt64(List<byte> bytes, long value)
    {
        var buffer = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        bytes.AddRange(buffer);
    }

    private static void PutEntry(List<byte> bytes, Guid lessonId, string path, long size, long ticks)
    {
        bytes.AddRange(lessonId.ToByteArray());
        PutString(bytes, path);
        PutInt64(bytes, size);
        PutInt64(bytes, ticks);
    }

    // A listing with no problem: version, current lesson, no problem, count, entries.
    private static byte[] ListingPayload(uint count, Action<List<byte>>? entries = null)
    {
        var bytes = new List<byte> { 1 };
        bytes.AddRange(Lesson.ToByteArray());
        bytes.Add(0);
        var countBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(countBytes, count);
        bytes.AddRange(countBytes);
        entries?.Invoke(bytes);
        return bytes.ToArray();
    }

    private static byte[] OneEntryListing(string path, long size, long ticks) =>
        ListingPayload(1, bytes => PutEntry(bytes, Lesson, path, size, ticks));

    private static readonly ClassFolderEntry[] Entries =
    {
        new(Lesson, MapPath, 1234, 638_000_000_000_000_000),
        new(Earlier, "provas/prova 1.docx", 0, 0),
        new(Lesson, "Exercícios – capítulo 3.pdf", FileTransfer.MaxFileSize, DateTime.MaxValue.Ticks),
    };

    [Fact]
    public void Listing_WithoutAProblem_RoundTrips()
    {
        var payload = ClassFolderWire.EncodeListing(Lesson, null, Entries);

        Assert.True(ClassFolderWire.TryDecodeListing(payload, out var current, out var problem, out var entries));
        Assert.Equal(Lesson, current);
        Assert.Null(problem);
        Assert.Equal(Entries, entries);
    }

    [Fact]
    public void Listing_WithAProblem_RoundTrips()
    {
        var payload = ClassFolderWire.EncodeListing(Lesson, "sem pasta", Array.Empty<ClassFolderEntry>());

        Assert.True(ClassFolderWire.TryDecodeListing(payload, out var current, out var problem, out var entries));
        Assert.Equal(Lesson, current);
        Assert.Equal("sem pasta", problem);
        Assert.Empty(entries);
    }

    [Fact]
    public void Listing_EmptyAndWithoutAProblem_RoundTrips()
    {
        var payload = ClassFolderWire.EncodeListing(Guid.Empty, null, Array.Empty<ClassFolderEntry>());

        Assert.True(ClassFolderWire.TryDecodeListing(payload, out var current, out var problem, out var entries));
        Assert.Equal(Guid.Empty, current);
        Assert.Null(problem);
        Assert.Empty(entries);
    }

    [Fact]
    public void EncodeListing_MatchesTheDocumentedLayout()
    {
        var expected = ListingPayload(1, bytes => PutEntry(bytes, Earlier, MapPath, 1234, 99));

        var payload = ClassFolderWire.EncodeListing(Lesson, null, new[] { new ClassFolderEntry(Earlier, MapPath, 1234, 99) });

        Assert.Equal(expected, payload);
    }

    [Fact]
    public void Listing_AProblemWithEntries_IsRejected()
    {
        var bytes = new List<byte> { 1 };
        bytes.AddRange(Lesson.ToByteArray());
        bytes.Add(1);
        PutString(bytes, "sem pasta");
        bytes.AddRange(new byte[] { 0, 0, 0, 1 });
        PutEntry(bytes, Lesson, MapPath, 1, 1);

        Assert.False(ClassFolderWire.TryDecodeListing(bytes.ToArray(), out _, out _, out _));
        Assert.Throws<ArgumentException>(() => ClassFolderWire.EncodeListing(Lesson, "sem pasta", Entries));
    }

    [Fact]
    public void TryDecodeListing_AcceptsExactlyMaxEntries()
    {
        var payload = ListingPayload(ClassFolderWire.MaxEntries, bytes =>
        {
            for (var i = 0; i < ClassFolderWire.MaxEntries; i++)
            {
                PutEntry(bytes, Lesson, "a", 1, 1);
            }
        });

        Assert.True(ClassFolderWire.TryDecodeListing(payload, out _, out _, out var entries));
        Assert.Equal(ClassFolderWire.MaxEntries, entries.Count);
    }

    [Fact]
    public void TryDecodeListing_RejectsMoreThanMaxEntries()
    {
        var payload = ListingPayload(ClassFolderWire.MaxEntries + 1, bytes =>
        {
            for (var i = 0; i < ClassFolderWire.MaxEntries + 1; i++)
            {
                PutEntry(bytes, Lesson, "a", 1, 1);
            }
        });

        Assert.False(ClassFolderWire.TryDecodeListing(payload, out _, out _, out var entries));
        Assert.Empty(entries);
    }

    [Fact]
    public void TryDecodeListing_ACountBiggerThanThePayload_IsRejected()
    {
        Assert.False(ClassFolderWire.TryDecodeListing(ListingPayload(uint.MaxValue), out _, out _, out _));
        Assert.False(ClassFolderWire.TryDecodeListing(ListingPayload(2, bytes => PutEntry(bytes, Lesson, "a", 1, 1)), out _, out _, out _));
    }

    [Fact]
    public void EncodeListing_MoreThanMaxEntries_Throws()
    {
        var entries = Enumerable.Repeat(new ClassFolderEntry(Lesson, "a", 1, 1), ClassFolderWire.MaxEntries + 1).ToArray();

        Assert.Throws<ArgumentException>(() => ClassFolderWire.EncodeListing(Lesson, null, entries));
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(FileTransfer.MaxFileSize + 1)]
    public void TryDecodeListing_RejectsASizeOutOfRange(long size)
    {
        Assert.False(ClassFolderWire.TryDecodeListing(OneEntryListing("a.txt", size, 1), out _, out _, out var entries));
        Assert.Empty(entries);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    public void TryDecodeListing_RejectsLastWriteTicksThatAreNotADate(long ticks)
    {
        Assert.False(ClassFolderWire.TryDecodeListing(OneEntryListing("a.txt", 1, ticks), out _, out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryDecodeListing_RejectsAnEmptyPath(string path)
    {
        Assert.False(ClassFolderWire.TryDecodeListing(OneEntryListing(path, 1, 1), out _, out _, out _));
    }

    [Fact]
    public void TryDecodeListing_RejectsAPathOverMaxNameBytes()
    {
        Assert.True(ClassFolderWire.TryDecodeListing(OneEntryListing(new string('a', FileTransfer.MaxNameBytes), 1, 1), out _, out _, out _));
        Assert.False(ClassFolderWire.TryDecodeListing(OneEntryListing(new string('a', FileTransfer.MaxNameBytes + 1), 1, 1), out _, out _, out _));
    }

    [Fact]
    public void TryDecodeListing_RejectsInvalidUtf8_InAPathAndInAProblem()
    {
        var path = new List<byte> { 1 };
        path.AddRange(Lesson.ToByteArray());
        path.Add(0);
        path.AddRange(new byte[] { 0, 0, 0, 1 });
        path.AddRange(Lesson.ToByteArray());
        PutString(path, new byte[] { 0x4F, 0xC3, 0x28 });
        PutInt64(path, 1);
        PutInt64(path, 1);

        var problem = new List<byte> { 1 };
        problem.AddRange(Lesson.ToByteArray());
        problem.Add(1);
        PutString(problem, new byte[] { 0x4F, 0xC3, 0x28 });
        problem.AddRange(new byte[] { 0, 0, 0, 0 });

        Assert.False(ClassFolderWire.TryDecodeListing(path.ToArray(), out _, out _, out _));
        Assert.False(ClassFolderWire.TryDecodeListing(problem.ToArray(), out _, out _, out _));
    }

    [Fact]
    public void TryDecodeListing_RejectsAProblemFlagThatIsNotABool()
    {
        var payload = ListingPayload(0);
        payload[17] = 2;

        Assert.False(ClassFolderWire.TryDecodeListing(payload, out _, out _, out _));
    }

    [Fact]
    public void FileRef_RoundTrips()
    {
        var payload = ClassFolderWire.EncodeFileRef(Lesson, MapPath);

        Assert.True(ClassFolderWire.TryDecodeFileRef(payload, out var lessonId, out var path));
        Assert.Equal(Lesson, lessonId);
        Assert.Equal(MapPath, path);
    }

    [Fact]
    public void EncodeFileRef_MatchesTheDocumentedLayout()
    {
        var expected = new List<byte> { 1 };
        expected.AddRange(Lesson.ToByteArray());
        PutString(expected, MapPath);

        Assert.Equal(expected.ToArray(), ClassFolderWire.EncodeFileRef(Lesson, MapPath));
    }

    [Fact]
    public void Rename_RoundTrips()
    {
        var payload = ClassFolderWire.EncodeRename(Lesson, MapPath, "fotos/mapa novo.png");

        Assert.True(ClassFolderWire.TryDecodeRename(payload, out var lessonId, out var path, out var newPath));
        Assert.Equal(Lesson, lessonId);
        Assert.Equal(MapPath, path);
        Assert.Equal("fotos/mapa novo.png", newPath);
    }

    [Fact]
    public void PushHeader_RoundTrips()
    {
        var payload = ClassFolderWire.EncodePushHeader(Lesson, MapPath, 123_456_789, 638_000_000_000_000_000);

        Assert.True(ClassFolderWire.TryDecodePushHeader(payload, out var lessonId, out var path, out var size, out var ticks));
        Assert.Equal(Lesson, lessonId);
        Assert.Equal(MapPath, path);
        Assert.Equal(123_456_789, size);
        Assert.Equal(638_000_000_000_000_000, ticks);
    }

    [Fact]
    public void PushHeader_EmptyFileIsAllowed()
    {
        var payload = ClassFolderWire.EncodePushHeader(Lesson, "vazio.txt", 0, 0);

        Assert.True(ClassFolderWire.TryDecodePushHeader(payload, out _, out _, out var size, out _));
        Assert.Equal(0, size);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(FileTransfer.MaxFileSize + 1)]
    public void TryDecodePushHeader_RejectsSizeOverMaxFileSize(long size)
    {
        var payload = ClassFolderWire.EncodePushHeader(Lesson, MapPath, 10, 10);
        var sizeOffset = 1 + 16 + 2 + Encoding.UTF8.GetByteCount(MapPath);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(sizeOffset), size);

        Assert.False(ClassFolderWire.TryDecodePushHeader(payload, out _, out _, out _, out _));
    }

    [Fact]
    public void TryDecodePushHeader_AcceptsExactlyMaxFileSize()
    {
        var payload = ClassFolderWire.EncodePushHeader(Lesson, MapPath, FileTransfer.MaxFileSize, 10);

        Assert.True(ClassFolderWire.TryDecodePushHeader(payload, out _, out _, out var size, out _));
        Assert.Equal(FileTransfer.MaxFileSize, size);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    public void TryDecodePushHeader_RejectsLastWriteTicksThatAreNotADate(long ticks)
    {
        var payload = ClassFolderWire.EncodePushHeader(Lesson, MapPath, 10, 10);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(payload.Length - 8), ticks);

        Assert.False(ClassFolderWire.TryDecodePushHeader(payload, out _, out _, out _, out _));
    }

    [Fact]
    public void LessonId_RoundTrips()
    {
        var payload = ClassFolderWire.EncodeLessonId(Lesson);

        Assert.Equal(17, payload.Length);
        Assert.True(ClassFolderWire.TryDecodeLessonId(payload, out var lessonId));
        Assert.Equal(Lesson, lessonId);
    }

    [Fact]
    public void TryDecodeFileRef_RejectsTruncatedPayload()
    {
        var payload = ClassFolderWire.EncodeFileRef(Lesson, MapPath);

        for (var length = 0; length < payload.Length; length++)
        {
            Assert.False(
                ClassFolderWire.TryDecodeFileRef(payload[..length], out _, out _),
                $"a payload cut to {length} of {payload.Length} bytes was accepted");
        }
    }

    // Every decoder, with a good payload, for the checks that apply to all.
    private static IEnumerable<(string Name, byte[] Payload, Func<byte[], bool> Decode)> EveryDecoder()
    {
        yield return ("listing",
            ClassFolderWire.EncodeListing(Lesson, null, Entries),
            p => ClassFolderWire.TryDecodeListing(p, out _, out _, out _));
        yield return ("listing with a problem",
            ClassFolderWire.EncodeListing(Lesson, "sem pasta", Array.Empty<ClassFolderEntry>()),
            p => ClassFolderWire.TryDecodeListing(p, out _, out _, out _));
        yield return ("file ref",
            ClassFolderWire.EncodeFileRef(Lesson, MapPath),
            p => ClassFolderWire.TryDecodeFileRef(p, out _, out _));
        yield return ("rename",
            ClassFolderWire.EncodeRename(Lesson, MapPath, "novo.png"),
            p => ClassFolderWire.TryDecodeRename(p, out _, out _, out _));
        yield return ("push header",
            ClassFolderWire.EncodePushHeader(Lesson, MapPath, 10, 10),
            p => ClassFolderWire.TryDecodePushHeader(p, out _, out _, out _, out _));
        yield return ("lesson id",
            ClassFolderWire.EncodeLessonId(Lesson),
            p => ClassFolderWire.TryDecodeLessonId(p, out _));
    }

    [Fact]
    public void EveryDecoder_AcceptsItsOwnEncoding_AndRejectsTrailingBytes_BadVersions_Truncation()
    {
        foreach (var (name, payload, decode) in EveryDecoder())
        {
            Assert.True(decode(payload), $"{name}: its own encoding was rejected");
            Assert.False(decode(payload.Append((byte)0).ToArray()), $"{name}: a trailing byte was accepted");

            foreach (var version in new byte[] { 0, 2, 255 })
            {
                var other = (byte[])payload.Clone();
                other[0] = version;
                Assert.False(decode(other), $"{name}: version {version} was accepted");
            }

            for (var length = 0; length < payload.Length; length++)
            {
                Assert.False(decode(payload[..length]), $"{name}: cut to {length} of {payload.Length} bytes, accepted");
            }
        }
    }

    [Fact]
    public void TryDecodeFileRef_FailureLeavesEmptyValues()
    {
        Assert.False(ClassFolderWire.TryDecodeFileRef(new byte[] { 9 }, out var lessonId, out var path));
        Assert.Equal(Guid.Empty, lessonId);
        Assert.Equal("", path);
    }

    [Fact]
    public void TryDecodeFileRef_RejectsInvalidUtf8_AnEmptyPath_AndALongPath()
    {
        byte[] Ref(Action<List<byte>> path)
        {
            var bytes = new List<byte> { 1 };
            bytes.AddRange(Lesson.ToByteArray());
            path(bytes);
            return bytes.ToArray();
        }

        Assert.False(ClassFolderWire.TryDecodeFileRef(Ref(b => PutString(b, new byte[] { 0x4F, 0xC3, 0x28 })), out _, out _));
        Assert.False(ClassFolderWire.TryDecodeFileRef(Ref(b => PutString(b, "")), out _, out _));
        Assert.False(ClassFolderWire.TryDecodeFileRef(Ref(b => PutString(b, new string('a', FileTransfer.MaxNameBytes + 1))), out _, out _));
        Assert.False(ClassFolderWire.TryDecodeFileRef(Ref(b => PutString(b, Encoding.UTF8.GetBytes("abc"), declaredLength: 4)), out _, out _));
        Assert.True(ClassFolderWire.TryDecodeFileRef(Ref(b => PutString(b, new string('a', FileTransfer.MaxNameBytes))), out _, out _));
    }

    [Fact]
    public void Encoders_RefuseAPathTheDecodersWouldRefuse()
    {
        var tooLong = new string('a', FileTransfer.MaxNameBytes + 1);

        Assert.Throws<ArgumentException>(() => ClassFolderWire.EncodeFileRef(Lesson, tooLong));
        Assert.Throws<ArgumentException>(() => ClassFolderWire.EncodeFileRef(Lesson, ""));
        Assert.Throws<ArgumentException>(() => ClassFolderWire.EncodeRename(Lesson, MapPath, tooLong));
        Assert.Throws<ArgumentException>(() => ClassFolderWire.EncodeRename(Lesson, "  ", "novo.png"));
        Assert.Throws<ArgumentException>(() => ClassFolderWire.EncodePushHeader(Lesson, tooLong, 1, 1));
        Assert.Throws<ArgumentException>(() => ClassFolderWire.EncodeListing(
            Lesson, null, new[] { new ClassFolderEntry(Lesson, tooLong, 1, 1) }));
    }

    [Fact]
    public void Encoders_RefuseASizeOrDateTheDecodersWouldRefuse()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ClassFolderWire.EncodePushHeader(Lesson, MapPath, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClassFolderWire.EncodePushHeader(Lesson, MapPath, FileTransfer.MaxFileSize + 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClassFolderWire.EncodePushHeader(Lesson, MapPath, 1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClassFolderWire.EncodeListing(
            Lesson, null, new[] { new ClassFolderEntry(Lesson, MapPath, FileTransfer.MaxFileSize + 1, 1) }));
    }
}
