using System.Buffers.Binary;
using System.Text;
using Midview.Protocol;
using Xunit;

public class ClassLessonTests
{
    private static readonly Guid Id = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    // The wire layout written out by hand, so a change to it fails here:
    // version, SavedAt, StartedAt, Ended, LessonId, Turma (uint16 length + UTF-8).
    private static byte[] Raw(byte version, byte ended, byte[] turma, ushort? declaredLength = null)
    {
        var payload = new byte[36 + turma.Length];
        payload[0] = version;
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(1), 1739000000);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(9), 1739000100);
        payload[17] = ended;
        Id.ToByteArray().CopyTo(payload, 18);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(34), declaredLength ?? (ushort)turma.Length);
        turma.CopyTo(payload, 36);
        return payload;
    }

    [Fact]
    public void EncodeThenDecode_RoundTrips()
    {
        var lesson = new ClassLesson(1739000000, Id, "3º A", 1739000100, false);

        Assert.True(ClassLesson.TryDecode(lesson.Encode(), out var decoded));
        Assert.Equal(lesson, decoded);
    }

    [Fact]
    public void Encode_MatchesTheDocumentedLayout()
    {
        var lesson = new ClassLesson(1739000000, Id, "3º A", 1739000100, true);

        Assert.Equal(Raw(1, 1, Encoding.UTF8.GetBytes("3º A")), lesson.Encode());
    }

    [Fact]
    public void EncodeThenDecode_EndedLessonAndEmptyTurma_RoundTrips()
    {
        var none = ClassLesson.None(42);

        Assert.True(ClassLesson.TryDecode(none.Encode(), out var decoded));
        Assert.Equal(none, decoded);
    }

    [Fact]
    public void EncodeThenDecode_TurmaAtTheLimit_RoundTrips()
    {
        var lesson = new ClassLesson(1, Id, new string('ã', ClassLesson.MaxTurmaLength), 2, false);

        Assert.True(ClassLesson.TryDecode(lesson.Encode(), out var decoded));
        Assert.Equal(lesson, decoded);
    }

    [Fact]
    public void Encode_TurmaOverTheLimit_Throws()
    {
        var lesson = new ClassLesson(1, Id, new string('a', ClassLesson.MaxTurmaLength + 1), 2, false);

        Assert.Throws<InvalidOperationException>(() => lesson.Encode());
    }

    [Fact]
    public void None_IsNotOn()
    {
        var none = ClassLesson.None(99);

        Assert.False(none.IsOn);
        Assert.True(none.Ended);
        Assert.Equal(Guid.Empty, none.LessonId);
        Assert.Equal("", none.Turma);
        Assert.Equal(0, none.StartedAt);
        Assert.Equal(99, none.SavedAt);
    }

    [Fact]
    public void IsOn_NeedsALessonThatHasNotEnded()
    {
        Assert.True(new ClassLesson(1, Id, "3º A", 2, false).IsOn);
        Assert.False(new ClassLesson(1, Id, "3º A", 2, true).IsOn);
        Assert.False(new ClassLesson(1, Guid.Empty, "3º A", 2, false).IsOn);
    }

    [Fact]
    public void FolderName_UsesTheEnDash()
    {
        var lesson = new ClassLesson(1, Id, "3º A", 2, false);

        Assert.Equal("Pasta da turma – 3º A", lesson.FolderName);
    }

    [Fact]
    public void StartedBefore_IsTrueOnlyForAnEarlierLocalDay()
    {
        var startedLate = new DateTime(2026, 10, 19, 23, 59, 0, DateTimeKind.Local);
        var lesson = new ClassLesson(1, Id, "3º A", new DateTimeOffset(startedLate).ToUnixTimeSeconds(), false);

        Assert.True(lesson.StartedBefore(new DateTime(2026, 10, 20, 0, 0, 1)));
        Assert.True(lesson.StartedBefore(new DateTime(2026, 10, 20)));
        Assert.True(lesson.StartedBefore(new DateTime(2026, 11, 3, 12, 0, 0)));
        Assert.False(lesson.StartedBefore(new DateTime(2026, 10, 19, 23, 59, 59)));
        Assert.False(lesson.StartedBefore(new DateTime(2026, 10, 19)));
        Assert.False(lesson.StartedBefore(new DateTime(2026, 10, 18)));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("trailing")]
    [InlineData("invalidUtf8")]
    [InlineData("longTurma")]
    [InlineData("endedNotABool")]
    [InlineData("turmaLengthPastTheEnd")]
    [InlineData("startedAtNegative")]
    [InlineData("startedAtOutOfRange")]
    [InlineData("truncated")]
    [InlineData("empty")]
    public void TryDecode_RejectsUnknownVersion_TrailingBytes_InvalidUtf8_LongTurma(string kind)
    {
        var ab = Encoding.UTF8.GetBytes("ab");
        var payload = kind switch
        {
            "version" => Raw(2, 0, ab),
            "trailing" => Raw(1, 0, ab).Append((byte)0).ToArray(),
            "invalidUtf8" => Raw(1, 0, new byte[] { 0x4F, 0xC3, 0x28 }),
            "longTurma" => Raw(1, 0, Encoding.UTF8.GetBytes(new string('a', ClassLesson.MaxTurmaLength + 1))),
            "endedNotABool" => Raw(1, 2, ab),
            "turmaLengthPastTheEnd" => Raw(1, 0, ab, declaredLength: 3),
            "startedAtNegative" => WithStartedAt(Raw(1, 0, ab), -1),
            "startedAtOutOfRange" => WithStartedAt(Raw(1, 0, ab), long.MaxValue),
            "truncated" => Raw(1, 0, ab)[..^1],
            "empty" => Array.Empty<byte>(),
            _ => throw new ArgumentException(kind),
        };

        Assert.False(ClassLesson.TryDecode(payload, out var lesson));
        Assert.Equal(ClassLesson.None(0), lesson);
    }

    private static byte[] WithStartedAt(byte[] payload, long startedAt)
    {
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(9), startedAt);
        return payload;
    }

    [Fact]
    public void TryDecode_TurmaAtTheLimit_IsAccepted()
    {
        var payload = Raw(1, 0, Encoding.UTF8.GetBytes(new string('ã', ClassLesson.MaxTurmaLength)));

        Assert.True(ClassLesson.TryDecode(payload, out var lesson));
        Assert.Equal(ClassLesson.MaxTurmaLength, lesson.Turma.Length);
    }

    private static string TempLessonPath() =>
        Path.Combine(Path.GetTempPath(), $"midview-lesson-{Guid.NewGuid()}", "class-lesson.json");

    private static void DeleteWithDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ClassLessonFile_DefaultPath_IsInProgramDataMidview()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Midview", "class-lesson.json");

        Assert.Equal(expected, ClassLessonFile.DefaultPath);
    }

    [Fact]
    public void ClassLessonFile_WriteThenRead_RoundTrips()
    {
        var path = TempLessonPath();
        try
        {
            var lesson = new ClassLesson(1739000000, Id, "3º A – manhã", 1739000100, false);

            ClassLessonFile.Write(path, lesson);

            Assert.Equal(lesson, ClassLessonFile.Read(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            DeleteWithDirectory(path);
        }
    }

    [Fact]
    public void ClassLessonFile_WriteTwice_KeepsTheLastOne()
    {
        var path = TempLessonPath();
        try
        {
            ClassLessonFile.Write(path, new ClassLesson(1, Id, "3º A", 10, false));
            var ended = new ClassLesson(2, Id, "3º A", 10, true);

            ClassLessonFile.Write(path, ended);

            Assert.Equal(ended, ClassLessonFile.Read(path));
        }
        finally
        {
            DeleteWithDirectory(path);
        }
    }

    [Fact]
    public void ClassLessonFile_WriteUsesTheDocumentedFieldNames()
    {
        var path = TempLessonPath();
        try
        {
            ClassLessonFile.Write(path, new ClassLesson(7, Id, "3º A", 9, true));

            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(path));
            var names = json.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray();
            Assert.Equal(new[] { "Ended", "LessonId", "SavedAt", "StartedAt", "Turma" }, names);
        }
        finally
        {
            DeleteWithDirectory(path);
        }
    }

    [Fact]
    public void ClassLessonFile_MissingFile_ReadsNull()
    {
        Assert.Null(ClassLessonFile.Read(TempLessonPath()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ this is not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"SavedAt\":\"yesterday\",\"LessonId\":\"x\",\"Turma\":\"3º A\",\"StartedAt\":1,\"Ended\":false}")]
    [InlineData("{\"SavedAt\":1,\"LessonId\":\"6f9619ff-8b86-d011-b42d-00c04fc964ff\",\"Turma\":null,\"StartedAt\":1,\"Ended\":false}")]
    [InlineData("{\"SavedAt\":1,\"LessonId\":\"6f9619ff-8b86-d011-b42d-00c04fc964ff\",\"Turma\":\"3º A\",\"StartedAt\":-5,\"Ended\":false}")]
    public void ClassLessonFile_CorruptFile_ReadsNull(string contents)
    {
        var path = TempLessonPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);

            Assert.Null(ClassLessonFile.Read(path));
        }
        finally
        {
            DeleteWithDirectory(path);
        }
    }
}
