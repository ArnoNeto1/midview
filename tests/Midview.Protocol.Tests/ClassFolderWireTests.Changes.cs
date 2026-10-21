using System.Buffers.Binary;
using System.Text;
using Midview.Protocol;
using Xunit;

// ClassFolderWire: the messages that change a student's files -- a push, with
// the state of the file it may replace, and clearing what was set aside, with
// the files the Master has dealt with.
public partial class ClassFolderWireTests
{
    private static readonly (long Size, long LastWriteTicks) PcVersion = (77, 638_000_000_000_000_001);

    [Fact]
    public void PushHeader_RoundTrips_OntoNothing_AndOntoAVersion()
    {
        foreach (var replacing in new (long, long)?[] { null, PcVersion })
        {
            var payload = ClassFolderWire.EncodePushHeader(Lesson, MapPath, 123_456_789, 638_000_000_000_000_000, replacing);

            Assert.True(ClassFolderWire.TryDecodePushHeader(
                payload, out var lessonId, out var path, out var size, out var ticks, out var decodedReplacing));
            Assert.Equal(Lesson, lessonId);
            Assert.Equal(MapPath, path);
            Assert.Equal(123_456_789, size);
            Assert.Equal(638_000_000_000_000_000, ticks);
            Assert.Equal(replacing, decodedReplacing);
        }
    }

    // Version, lesson, path, size, time, then 0 ("the PC must have no such file") or
    // 1 and the size and time the PC's file must have.
    [Fact]
    public void EncodePushHeader_MatchesTheDocumentedLayout()
    {
        List<byte> Head()
        {
            var bytes = new List<byte> { 1 };
            bytes.AddRange(Lesson.ToByteArray());
            PutString(bytes, MapPath);
            PutInt64(bytes, 10);
            PutInt64(bytes, 20);
            return bytes;
        }

        var ontoNothing = Head();
        ontoNothing.Add(0);
        var ontoAVersion = Head();
        ontoAVersion.Add(1);
        PutInt64(ontoAVersion, 30);
        PutInt64(ontoAVersion, 40);

        Assert.Equal(ontoNothing.ToArray(), ClassFolderWire.EncodePushHeader(Lesson, MapPath, 10, 20, null));
        Assert.Equal(ontoAVersion.ToArray(), ClassFolderWire.EncodePushHeader(Lesson, MapPath, 10, 20, (30, 40)));
    }

    [Fact]
    public void PushHeader_EmptyFileIsAllowed()
    {
        var payload = ClassFolderWire.EncodePushHeader(Lesson, "vazio.txt", 0, 0, (0, 0));

        Assert.True(ClassFolderWire.TryDecodePushHeader(payload, out _, out _, out var size, out _, out var replacing));
        Assert.Equal(0, size);
        Assert.Equal((0L, 0L), replacing);
    }

    private static int SizeOffset => 1 + 16 + 2 + Encoding.UTF8.GetByteCount(MapPath);

    [Theory]
    [InlineData(-1L)]
    [InlineData(FileTransfer.MaxFileSize + 1)]
    public void TryDecodePushHeader_RejectsSizeOverMaxFileSize(long size)
    {
        var payload = ClassFolderWire.EncodePushHeader(Lesson, MapPath, 10, 10, null);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(SizeOffset), size);

        Assert.False(ClassFolderWire.TryDecodePushHeader(payload, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void TryDecodePushHeader_AcceptsExactlyMaxFileSize()
    {
        var payload = ClassFolderWire.EncodePushHeader(Lesson, MapPath, FileTransfer.MaxFileSize, 10, (FileTransfer.MaxFileSize, 10));

        Assert.True(ClassFolderWire.TryDecodePushHeader(payload, out _, out _, out var size, out _, out _));
        Assert.Equal(FileTransfer.MaxFileSize, size);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    public void TryDecodePushHeader_RejectsLastWriteTicksThatAreNotADate(long ticks)
    {
        var payload = ClassFolderWire.EncodePushHeader(Lesson, MapPath, 10, 10, null);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(SizeOffset + 8), ticks);

        Assert.False(ClassFolderWire.TryDecodePushHeader(payload, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void TryDecodePushHeader_RejectsAReplacedStateItCantRead()
    {
        var badFlag = ClassFolderWire.EncodePushHeader(Lesson, MapPath, 10, 10, null);
        badFlag[^1] = 2;
        var badSize = ClassFolderWire.EncodePushHeader(Lesson, MapPath, 10, 10, (5, 5));
        BinaryPrimitives.WriteInt64BigEndian(badSize.AsSpan(badSize.Length - 16), -1);
        var badTicks = ClassFolderWire.EncodePushHeader(Lesson, MapPath, 10, 10, (5, 5));
        BinaryPrimitives.WriteInt64BigEndian(badTicks.AsSpan(badTicks.Length - 8), long.MaxValue);

        Assert.False(ClassFolderWire.TryDecodePushHeader(badFlag, out _, out _, out _, out _, out var replacing));
        Assert.Null(replacing);
        Assert.False(ClassFolderWire.TryDecodePushHeader(badSize, out _, out _, out _, out _, out _));
        Assert.False(ClassFolderWire.TryDecodePushHeader(badTicks, out _, out _, out _, out _, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClassFolderWire.EncodePushHeader(Lesson, MapPath, 1, 1, (-1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClassFolderWire.EncodePushHeader(Lesson, MapPath, 1, 1, (1, -1)));
    }

    [Fact]
    public void ClearSetAside_RoundTrips()
    {
        var files = new[]
        {
            new ClassFolderEntry(Lesson, MapPath, 1234, 638_000_000_000_000_000),
            new ClassFolderEntry(Lesson, "provas/prova 1.docx", 0, 0),
        };

        var payload = ClassFolderWire.EncodeClearSetAside(Lesson, files);

        Assert.True(ClassFolderWire.TryDecodeClearSetAside(payload, out var lessonId, out var decoded));
        Assert.Equal(Lesson, lessonId);
        Assert.Equal(files, decoded);
    }

    // Version, lesson, count, then each file: path, size, time.
    [Fact]
    public void EncodeClearSetAside_MatchesTheDocumentedLayout()
    {
        var expected = new List<byte> { 1 };
        expected.AddRange(Lesson.ToByteArray());
        expected.AddRange(new byte[] { 0, 0, 0, 1 });
        PutString(expected, MapPath);
        PutInt64(expected, 5);
        PutInt64(expected, 6);

        Assert.Equal(expected.ToArray(), ClassFolderWire.EncodeClearSetAside(Lesson, new[] { new ClassFolderEntry(Lesson, MapPath, 5, 6) }));
    }

    [Fact]
    public void ClearSetAside_CarriesAtMostMaxEntries_OfThatLessonOnly()
    {
        var most = Enumerable.Repeat(new ClassFolderEntry(Lesson, "a", 1, 1), ClassFolderWire.MaxEntries).ToArray();
        var tooMany = most.Append(new ClassFolderEntry(Lesson, "b", 1, 1)).ToArray();

        Assert.True(ClassFolderWire.TryDecodeClearSetAside(ClassFolderWire.EncodeClearSetAside(Lesson, most), out _, out var decoded));
        Assert.Equal(ClassFolderWire.MaxEntries, decoded.Count);
        Assert.Throws<ArgumentException>(() => ClassFolderWire.EncodeClearSetAside(Lesson, tooMany));
        Assert.Throws<ArgumentException>(() => ClassFolderWire.EncodeClearSetAside(Lesson, new[] { new ClassFolderEntry(Earlier, "a", 1, 1) }));

        var count = new List<byte> { 1 };
        count.AddRange(Lesson.ToByteArray());
        count.AddRange(new byte[] { 0, 0, 0x27, 0x11 }); // MaxEntries + 1
        Assert.False(ClassFolderWire.TryDecodeClearSetAside(count.ToArray(), out _, out _));
    }

    [Fact]
    public void TryDecodeClearSetAside_RejectsAFileItCantRead()
    {
        byte[] OneFile(string path, long size, long ticks)
        {
            var bytes = new List<byte> { 1 };
            bytes.AddRange(Lesson.ToByteArray());
            bytes.AddRange(new byte[] { 0, 0, 0, 1 });
            PutString(bytes, path);
            PutInt64(bytes, size);
            PutInt64(bytes, ticks);
            return bytes.ToArray();
        }

        Assert.True(ClassFolderWire.TryDecodeClearSetAside(OneFile("a.txt", 1, 1), out _, out _));
        Assert.False(ClassFolderWire.TryDecodeClearSetAside(OneFile("", 1, 1), out _, out _));
        Assert.False(ClassFolderWire.TryDecodeClearSetAside(OneFile("a.txt", -1, 1), out _, out _));
        Assert.False(ClassFolderWire.TryDecodeClearSetAside(OneFile("a.txt", 1, long.MaxValue), out _, out var files));
        Assert.Empty(files);
    }
}
