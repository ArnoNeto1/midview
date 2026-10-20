using System.Buffers.Binary;
using Midview.Protocol;
using Xunit;

public class ClassFolderSyncTests : IDisposable
{
    private static readonly long Monday = new DateTime(2026, 10, 19, 13, 0, 0, DateTimeKind.Utc).Ticks;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"midview-classfolder-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Request_RoundTrips_AccentsAndAnEmptyFolderIncluded()
    {
        var lesson = Guid.NewGuid();
        var entries = new[]
        {
            new ClassFolderEntry("redação.docx", 1234, Monday),
            new ClassFolderEntry("site/imagens/foto.jpg", 0, Monday + 1),
        };

        Assert.True(ClassFolderSync.TryDecodeRequest(
            ClassFolderSync.EncodeRequest(lesson, "7º A", entries), out var decodedLesson, out var className, out var decoded));
        Assert.Equal(lesson, decodedLesson);
        Assert.Equal("7º A", className);
        Assert.Equal(entries, decoded);

        Assert.True(ClassFolderSync.TryDecodeRequest(
            ClassFolderSync.EncodeRequest(lesson, string.Empty, Array.Empty<ClassFolderEntry>()), out _, out _, out var none));
        Assert.Empty(none);
    }

    [Fact]
    public void Request_WithoutALesson_OrCutShort_IsRefused()
    {
        var payload = ClassFolderSync.EncodeRequest(Guid.NewGuid(), "6º B", new[] { new ClassFolderEntry("a.txt", 1, Monday) });

        Assert.False(ClassFolderSync.TryDecodeRequest(payload[..^1], out _, out _, out _));
        Assert.False(ClassFolderSync.TryDecodeRequest(payload.Concat(new byte[] { 0 }).ToArray(), out _, out _, out _));
        Assert.False(ClassFolderSync.TryDecodeRequest(
            ClassFolderSync.EncodeRequest(Guid.Empty, "6º B", Array.Empty<ClassFolderEntry>()), out _, out _, out _));
        Assert.False(ClassFolderSync.TryDecodeRequest(new byte[10], out _, out _, out _));
    }

    [Fact]
    public void Listing_WithImpossibleValues_IsRefused()
    {
        Assert.False(Decodes(new ClassFolderEntry("a.txt", -1, Monday)));
        Assert.False(Decodes(new ClassFolderEntry("a.txt", FileTransfer.MaxFileSize + 1, Monday)));
        Assert.False(Decodes(new ClassFolderEntry("a.txt", 1, 0)));
        Assert.False(Decodes(new ClassFolderEntry("   ", 1, Monday)));

        var negativeCount = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(negativeCount, -1);
        Assert.False(ClassFolderSync.TryDecodeListing(negativeCount, out _));
        var tooMany = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(tooMany, ClassFolderSync.MaxEntries + 1);
        Assert.False(ClassFolderSync.TryDecodeListing(tooMany, out _));

        var invalidUtf8 = ClassFolderSync.EncodeListing(new[] { new ClassFolderEntry("ab", 1, Monday) });
        invalidUtf8[^1] = 0xFF;
        Assert.False(ClassFolderSync.TryDecodeListing(invalidUtf8, out _));
    }

    [Fact]
    public void Wanted_RoundTrips_AndMustPointIntoTheRequestOnce()
    {
        Assert.True(ClassFolderSync.TryDecodeWanted(ClassFolderSync.EncodeWanted(new[] { 2, 0 }), 3, out var wanted));
        Assert.Equal(new[] { 2, 0 }, wanted);

        Assert.False(ClassFolderSync.TryDecodeWanted(ClassFolderSync.EncodeWanted(new[] { 3 }), 3, out _));
        Assert.False(ClassFolderSync.TryDecodeWanted(ClassFolderSync.EncodeWanted(new[] { -1 }), 3, out _));
        Assert.False(ClassFolderSync.TryDecodeWanted(ClassFolderSync.EncodeWanted(new[] { 1, 1 }), 3, out _));
        Assert.False(ClassFolderSync.TryDecodeWanted(ClassFolderSync.EncodeWanted(new[] { 0 })[..^1], 3, out _));
    }

    [Fact]
    public void List_GivesSafePathsInOrder_AndSkipsWhatCantBeSavedElsewhere()
    {
        Write("b.txt", 2);
        Write("A.txt", 1);
        Write("site/imagens/logo.png", 3);
        Write(".midview-0123.parcial", 9);
        Write("deep/" + string.Join("/", Enumerable.Range(1, FileCollection.MaxDepth)) + ".txt", 1);
        File.SetAttributes(Write(".oculto.docx", 1), FileAttributes.Hidden); // like Office's "~$" lock files

        var files = ClassFolderFiles.List(_directory);

        Assert.Equal(new[] { "A.txt", "b.txt", "site/imagens/logo.png" }, files.Select(file => file.Entry.Path));
        Assert.Equal(3, files[2].Entry.Size);
        Assert.Equal(File.GetLastWriteTimeUtc(Path.Combine(_directory, "b.txt")).Ticks, files[1].Entry.LastWriteTicks);
    }

    [Theory]
    [InlineData("trabalho.docx", true)]
    [InlineData("site/imagens/foto.jpg", true)]
    [InlineData("../fora.txt", false)]
    [InlineData("site/../fora.txt", false)]
    [InlineData(@"site\fora.txt", false)]
    [InlineData("C:/Windows/evil.exe", false)]
    [InlineData("pasta/CON", false)]
    [InlineData("pasta/.midview-x.parcial", false)]
    public void TargetPath_OnlyForPathsListCouldGive(string path, bool allowed)
    {
        var target = ClassFolderFiles.TargetPath(_directory, path);

        if (allowed)
        {
            Assert.Equal(Path.Combine(new[] { _directory }.Concat(path.Split('/')).ToArray()), target);
        }
        else
        {
            Assert.Null(target);
        }
    }

    private static bool Decodes(ClassFolderEntry entry)
    {
        var payload = new List<byte>();
        var path = System.Text.Encoding.UTF8.GetBytes(entry.Path);
        var header = new byte[4 + 18];
        BinaryPrimitives.WriteInt32BigEndian(header, 1);
        BinaryPrimitives.WriteInt64BigEndian(header.AsSpan(4), entry.Size);
        BinaryPrimitives.WriteInt64BigEndian(header.AsSpan(12), entry.LastWriteTicks);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(20), (ushort)path.Length);
        payload.AddRange(header);
        payload.AddRange(path);
        return ClassFolderSync.TryDecodeListing(payload.ToArray(), out _);
    }

    private string Write(string relativePath, int size)
    {
        var path = Path.Combine(new[] { _directory }.Concat(relativePath.Split('/')).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
        return path;
    }
}
