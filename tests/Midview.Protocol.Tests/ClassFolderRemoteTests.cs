using System.Text;
using System.Text.Json;
using Midview.Protocol;
using Xunit;

public class ClassFolderRemoteTests
{
    private static readonly Guid Id = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);

    private static RemoteState State(bool withLessonFiles = true) => new(
        "PC-PROF-1",
        new ClassLessonDto(1739000000, Id, "3º A", 1739000100, false),
        new[]
        {
            new ClassSummaryDto("3º A", 12, 3_500_000, new DateOnly(2026, 10, 21)),
            new ClassSummaryDto("4º B", 0, 0, null),
        },
        withLessonFiles ? new LessonFilesDto("3º A", 12, 3_500_000, 2, 40_000) : null,
        new[]
        {
            new PcLineDto("LAB-01", "InSync", ""),
            new PcLineDto("LAB-02", "Failed", "Midview antigo"),
        },
        new[] { "LAB-03: sem espaço" },
        long.MaxValue,
        5_000_000_000);

    private static RemoteState EmptyState() => new(
        "PC-PROF-1",
        new ClassLessonDto(5, Guid.Empty, "", 0, true),
        Array.Empty<ClassSummaryDto>(),
        null,
        Array.Empty<PcLineDto>(),
        Array.Empty<string>(),
        0,
        0);

    private const string ValidStateJson =
        "{\"homeName\":\"PC\",\"lesson\":{\"savedAt\":1,\"lessonId\":\"6f9619ff-8b86-d011-b42d-00c04fc964ff\",\"turma\":\"3º A\",\"startedAt\":2,\"ended\":false},"
        + "\"classes\":[],\"lessonFiles\":null,\"pcs\":[],\"lastEndProblems\":[],\"freeBytes\":1,\"recycleBinBytes\":2}";

    [Fact]
    public void Constants_AreTheOnesTheOtherSidesAgreeOn()
    {
        Assert.Equal(28903, ClassFolderRemote.Port);
        Assert.Equal(4 * 1024 * 1024, ClassFolderRemote.MaxJsonBytes);
    }

    [Fact]
    public void RemoteState_RoundTrips()
    {
        var state = State();

        var decoded = ClassFolderRemote.DecodeJson<RemoteState>(ClassFolderRemote.EncodeJson(state));

        Assert.NotNull(decoded);
        Assert.Equal(state.HomeName, decoded.HomeName);
        Assert.Equal(state.Lesson, decoded.Lesson);
        Assert.Equal(state.Classes, decoded.Classes);
        Assert.Equal(state.LessonFiles, decoded.LessonFiles);
        Assert.Equal(state.Pcs, decoded.Pcs);
        Assert.Equal(state.LastEndProblems, decoded.LastEndProblems);
        Assert.Equal(state.FreeBytes, decoded.FreeBytes);
        Assert.Equal(state.RecycleBinBytes, decoded.RecycleBinBytes);
    }

    [Fact]
    public void RemoteState_WithoutLessonFiles_RoundTrips()
    {
        var decoded = ClassFolderRemote.DecodeJson<RemoteState>(ClassFolderRemote.EncodeJson(State(withLessonFiles: false)));

        Assert.NotNull(decoded);
        Assert.Null(decoded.LessonFiles);
    }

    [Fact]
    public void RemoteState_NothingToShow_RoundTrips()
    {
        var decoded = ClassFolderRemote.DecodeJson<RemoteState>(ClassFolderRemote.EncodeJson(EmptyState()));

        Assert.NotNull(decoded);
        Assert.Empty(decoded.Classes);
        Assert.Empty(decoded.Pcs);
        Assert.Empty(decoded.LastEndProblems);
        Assert.False(decoded.Lesson.ToLesson().IsOn);
    }

    [Fact]
    public void EncodeJson_UsesCamelCaseNames()
    {
        using var json = JsonDocument.Parse(ClassFolderRemote.EncodeJson(State()));

        Assert.Equal(
            new[] { "classes", "freeBytes", "homeName", "lastEndProblems", "lesson", "lessonFiles", "pcs", "recycleBinBytes" },
            json.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            new[] { "ended", "lessonId", "savedAt", "startedAt", "turma" },
            json.RootElement.GetProperty("lesson").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("2026-10-21", json.RootElement.GetProperty("classes")[0].GetProperty("lastLesson").GetString());
    }

    [Fact]
    public void ClassLessonDto_ConvertsToAndFromTheLesson()
    {
        var lesson = new ClassLesson(1739000000, Id, "3º A", 1739000100, false);

        var dto = ClassLessonDto.From(lesson);

        Assert.Equal(new ClassLessonDto(1739000000, Id, "3º A", 1739000100, false), dto);
        Assert.Equal(lesson, dto.ToLesson());
    }

    [Theory]
    [InlineData("start", "3º A", null, null)]
    [InlineData("create", "4º B", null, null)]
    [InlineData("rename", "4º B", "4º C", null)]
    [InlineData("delete", "4º B", null, null)]
    [InlineData("end", null, null, "keep")]
    [InlineData("end", null, null, "today")]
    [InlineData("end", null, null, "all")]
    public void RemoteCommand_RoundTrips(string kind, string? turma, string? newName, string? choice)
    {
        var command = new RemoteCommand(kind, turma, newName, choice);

        Assert.Equal(command, ClassFolderRemote.DecodeJson<RemoteCommand>(ClassFolderRemote.EncodeJson(command)));
    }

    [Fact]
    public void RemoteCommand_NamesItsKindsAndChoices()
    {
        Assert.Equal("start", RemoteCommand.Start);
        Assert.Equal("end", RemoteCommand.End);
        Assert.Equal("create", RemoteCommand.Create);
        Assert.Equal("rename", RemoteCommand.Rename);
        Assert.Equal("delete", RemoteCommand.Delete);
        Assert.Equal("keep", RemoteCommand.Keep);
        Assert.Equal("today", RemoteCommand.Today);
        Assert.Equal("all", RemoteCommand.All);
    }

    [Fact]
    public void RemoteReply_RoundTrips()
    {
        var reply = new RemoteReply(
            false,
            "Em 1 PC não deu",
            new[] { new ResultLineDto("LAB-01", true, ""), new ResultLineDto("LAB-02", false, "desligado") },
            "3º A");

        var decoded = ClassFolderRemote.DecodeJson<RemoteReply>(ClassFolderRemote.EncodeJson(reply));

        Assert.NotNull(decoded);
        Assert.Equal(reply.Ok, decoded.Ok);
        Assert.Equal(reply.Detail, decoded.Detail);
        Assert.Equal(reply.Lines, decoded.Lines);
        Assert.Equal(reply.Turma, decoded.Turma);
    }

    [Fact]
    public void RemoteReply_WithoutLinesOrTurma_RoundTrips()
    {
        var reply = new RemoteReply(true, "", Array.Empty<ResultLineDto>(), null);

        var decoded = ClassFolderRemote.DecodeJson<RemoteReply>(ClassFolderRemote.EncodeJson(reply));

        Assert.NotNull(decoded);
        Assert.True(decoded.Ok);
        Assert.Empty(decoded.Lines);
        Assert.Null(decoded.Turma);
    }

    [Fact]
    public void EachDto_RoundTrips()
    {
        var summary = new ClassSummaryDto("3º A", 12, 3_500_000, new DateOnly(2026, 10, 21));
        var files = new LessonFilesDto("3º A", 12, 3_500_000, 2, 40_000);
        var pc = new PcLineDto("LAB-01", "Copying", "3 arquivos");
        var result = new ResultLineDto("LAB-01", false, "sem espaço");
        var lesson = new ClassLessonDto(1, Id, "3º A", 2, true);

        Assert.Equal(summary, ClassFolderRemote.DecodeJson<ClassSummaryDto>(ClassFolderRemote.EncodeJson(summary)));
        Assert.Equal(files, ClassFolderRemote.DecodeJson<LessonFilesDto>(ClassFolderRemote.EncodeJson(files)));
        Assert.Equal(pc, ClassFolderRemote.DecodeJson<PcLineDto>(ClassFolderRemote.EncodeJson(pc)));
        Assert.Equal(result, ClassFolderRemote.DecodeJson<ResultLineDto>(ClassFolderRemote.EncodeJson(result)));
        Assert.Equal(lesson, ClassFolderRemote.DecodeJson<ClassLessonDto>(ClassFolderRemote.EncodeJson(lesson)));
    }

    [Fact]
    public void DecodeJson_AValidState_IsAccepted()
    {
        Assert.NotNull(ClassFolderRemote.DecodeJson<RemoteState>(Bytes(ValidStateJson)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ this is not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("\"text\"")]
    [InlineData("{\"kind\":\"start\"")]
    public void DecodeJson_MalformedJson_IsNull(string json)
    {
        Assert.Null(ClassFolderRemote.DecodeJson<RemoteCommand>(Bytes(json)));
        Assert.Null(ClassFolderRemote.DecodeJson<RemoteState>(Bytes(json)));
        Assert.Null(ClassFolderRemote.DecodeJson<RemoteReply>(Bytes(json)));
    }

    [Fact]
    public void DecodeJson_InvalidUtf8_IsNull()
    {
        Assert.Null(ClassFolderRemote.DecodeJson<RemoteCommand>(new byte[] { 0x7B, 0x22, 0xC3, 0x28, 0x22, 0x7D }));
    }

    [Fact]
    public void DecodeJson_InvalidUtf8InAValue_IsNull()
    {
        var json = Encoding.UTF8.GetBytes("{\"kind\":\"start\",\"turma\":\"")
            .Concat(new byte[] { 0xC3, 0x28 })
            .Concat(Encoding.UTF8.GetBytes("\"}"))
            .ToArray();

        Assert.Null(ClassFolderRemote.DecodeJson<RemoteCommand>(json));
    }

    [Theory]
    [InlineData(",\"freeBytes\":1", "")]
    [InlineData(",\"recycleBinBytes\":2", "")]
    [InlineData("\"savedAt\":1,", "")]
    [InlineData(",\"startedAt\":2", "")]
    [InlineData(",\"ended\":false", "")]
    [InlineData("\"lessonId\":\"6f9619ff-8b86-d011-b42d-00c04fc964ff\",", "")]
    public void DecodeJson_AStateMissingANumberOrFlag_IsNull(string original, string replacement)
    {
        // Not read as zero or false: a missing field is a state that isn't one.
        Assert.Contains(original, ValidStateJson);
        Assert.Null(ClassFolderRemote.DecodeJson<RemoteState>(Bytes(ValidStateJson.Replace(original, replacement))));
    }

    [Theory]
    [InlineData("{\"detail\":\"\",\"lines\":[]}")]
    [InlineData("{\"ok\":true,\"detail\":\"\",\"lines\":[{\"name\":\"PC\",\"detail\":\"\"}]}")]
    public void DecodeJson_AReplyMissingAFlag_IsNull(string json)
    {
        Assert.Null(ClassFolderRemote.DecodeJson<RemoteReply>(Bytes(json)));
    }

    [Theory]
    [InlineData("{\"kind\":\"begin\",\"turma\":\"3º A\"}")]
    [InlineData("{\"kind\":\"Start\",\"turma\":\"3º A\"}")]
    [InlineData("{\"kind\":\"\",\"turma\":\"3º A\"}")]
    [InlineData("{\"turma\":\"3º A\"}")]
    [InlineData("{\"kind\":null}")]
    [InlineData("{\"kind\":5}")]
    public void DecodeJson_UnknownOrMissingKind_IsNull(string json)
    {
        Assert.Null(ClassFolderRemote.DecodeJson<RemoteCommand>(Bytes(json)));
    }

    [Theory]
    [InlineData("{\"kind\":\"end\",\"choice\":\"everything\"}")]
    [InlineData("{\"kind\":\"end\",\"choice\":\"Keep\"}")]
    [InlineData("{\"kind\":\"end\",\"choice\":\"\"}")]
    [InlineData("{\"kind\":\"end\",\"choice\":7}")]
    public void DecodeJson_UnknownChoice_IsNull(string json)
    {
        Assert.Null(ClassFolderRemote.DecodeJson<RemoteCommand>(Bytes(json)));
    }

    [Fact]
    public void DecodeJson_ACommandWithoutChoiceOrNames_IsAccepted()
    {
        var command = ClassFolderRemote.DecodeJson<RemoteCommand>(Bytes("{\"kind\":\"end\"}"));

        Assert.Equal(new RemoteCommand("end", null, null, null), command);
    }

    [Fact]
    public void DecodeJson_WrongTypes_AreNull()
    {
        Assert.Null(ClassFolderRemote.DecodeJson<RemoteReply>(Bytes("{\"ok\":\"yes\",\"detail\":\"\",\"lines\":[]}")));
        Assert.Null(ClassFolderRemote.DecodeJson<RemoteReply>(Bytes("{\"ok\":true,\"detail\":\"\",\"lines\":{}}")));
        Assert.Null(ClassFolderRemote.DecodeJson<RemoteState>(Bytes(ValidStateJson.Replace("\"freeBytes\":1", "\"freeBytes\":\"1\""))));
        Assert.Null(ClassFolderRemote.DecodeJson<RemoteState>(Bytes(ValidStateJson.Replace("6f9619ff-8b86-d011-b42d-00c04fc964ff", "not-a-guid"))));
        Assert.Null(ClassFolderRemote.DecodeJson<RemoteState>(Bytes(ValidStateJson.Replace("\"classes\":[]", "\"classes\":[{\"turma\":\"A\",\"files\":0,\"bytes\":0,\"lastLesson\":\"yesterday\"}]"))));
    }

    [Fact]
    public void DecodeJson_AReplyMissingWhatItNeeds_IsNull()
    {
        Assert.Null(ClassFolderRemote.DecodeJson<RemoteReply>(Bytes("{\"ok\":true,\"lines\":[]}")));
        Assert.Null(ClassFolderRemote.DecodeJson<RemoteReply>(Bytes("{\"ok\":true,\"detail\":\"\"}")));
        Assert.Null(ClassFolderRemote.DecodeJson<RemoteReply>(Bytes("{\"ok\":true,\"detail\":null,\"lines\":[]}")));
        Assert.Null(ClassFolderRemote.DecodeJson<RemoteReply>(Bytes("{\"ok\":true,\"detail\":\"\",\"lines\":[null]}")));
        Assert.Null(ClassFolderRemote.DecodeJson<RemoteReply>(Bytes("{\"ok\":true,\"detail\":\"\",\"lines\":[{\"ok\":true,\"detail\":\"\"}]}")));
        Assert.Null(ClassFolderRemote.DecodeJson<RemoteReply>(Bytes("{\"ok\":true,\"detail\":\"\",\"lines\":[{\"name\":\"PC\",\"ok\":true}]}")));
    }

    [Theory]
    [InlineData("\"homeName\":\"PC\"", "\"homeName\":null")]
    [InlineData("\"homeName\":\"PC\"", "")]
    [InlineData("\"classes\":[]", "\"classes\":null")]
    [InlineData("\"classes\":[]", "\"classes\":[null]")]
    [InlineData("\"classes\":[]", "\"classes\":[{\"files\":0,\"bytes\":0}]")]
    [InlineData("\"classes\":[]", "\"classes\":[{\"turma\":\"A\",\"files\":-1,\"bytes\":0}]")]
    [InlineData("\"classes\":[]", "\"classes\":[{\"turma\":\"A\",\"files\":0,\"bytes\":-1}]")]
    [InlineData("\"pcs\":[]", "\"pcs\":null")]
    [InlineData("\"pcs\":[]", "\"pcs\":[null]")]
    [InlineData("\"pcs\":[]", "\"pcs\":[{\"name\":null,\"state\":\"InSync\",\"detail\":\"\"}]")]
    [InlineData("\"pcs\":[]", "\"pcs\":[{\"name\":\"PC\",\"state\":null,\"detail\":\"\"}]")]
    [InlineData("\"pcs\":[]", "\"pcs\":[{\"name\":\"PC\",\"state\":\"InSync\"}]")]
    [InlineData("\"lastEndProblems\":[]", "\"lastEndProblems\":null")]
    [InlineData("\"lastEndProblems\":[]", "\"lastEndProblems\":[null]")]
    [InlineData("\"freeBytes\":1", "\"freeBytes\":-1")]
    [InlineData("\"recycleBinBytes\":2", "\"recycleBinBytes\":-2")]
    [InlineData("\"lessonFiles\":null", "\"lessonFiles\":{\"turma\":\"A\",\"files\":-1,\"bytes\":0,\"today\":0,\"todayBytes\":0}")]
    [InlineData("\"lessonFiles\":null", "\"lessonFiles\":{\"turma\":\"A\",\"files\":1,\"bytes\":0,\"today\":-1,\"todayBytes\":0}")]
    [InlineData("\"lessonFiles\":null", "\"lessonFiles\":{\"files\":1,\"bytes\":0,\"today\":0,\"todayBytes\":0}")]
    public void DecodeJson_AStateThatIsNotOne_IsNull(string original, string replacement)
    {
        var json = ValidStateJson.Replace(original, replacement).Replace(",,", ",").Replace("{,", "{");

        Assert.Null(ClassFolderRemote.DecodeJson<RemoteState>(Bytes(json)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"lesson\":null,")]
    public void DecodeJson_AStateWithoutALesson_IsNull(string lesson)
    {
        var json = "{\"homeName\":\"PC\"," + lesson
            + "\"classes\":[],\"lessonFiles\":null,\"pcs\":[],\"lastEndProblems\":[],\"freeBytes\":1,\"recycleBinBytes\":2}";

        Assert.Null(ClassFolderRemote.DecodeJson<RemoteState>(Bytes(json)));
    }

    [Theory]
    [InlineData("\"turma\":\"3º A\"", "\"turma\":null")]
    [InlineData("\"turma\":\"3º A\"", "\"turma\":\"" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" + "\"")]
    [InlineData("\"startedAt\":2", "\"startedAt\":-1")]
    [InlineData("\"startedAt\":2", "\"startedAt\":9223372036854775807")]
    public void DecodeJson_ALessonThatIsNotOne_IsNull(string original, string replacement)
    {
        Assert.Null(ClassFolderRemote.DecodeJson<RemoteState>(Bytes(ValidStateJson.Replace(original, replacement))));
    }

    [Fact]
    public void DecodeJson_AHomeNameOverTheLimit_IsNull()
    {
        var json = ValidStateJson.Replace("\"homeName\":\"PC\"", $"\"homeName\":\"{new string('a', ClassHome.MaxNameLength + 1)}\"");
        var atTheLimit = ValidStateJson.Replace("\"homeName\":\"PC\"", $"\"homeName\":\"{new string('a', ClassHome.MaxNameLength)}\"");

        Assert.Null(ClassFolderRemote.DecodeJson<RemoteState>(Bytes(json)));
        Assert.NotNull(ClassFolderRemote.DecodeJson<RemoteState>(Bytes(atTheLimit)));
    }

    [Fact]
    public void DecodeJson_APayloadOverTheLimit_IsNull()
    {
        var atTheLimit = PaddedTo(ClassFolderRemote.MaxJsonBytes);
        var overTheLimit = PaddedTo(ClassFolderRemote.MaxJsonBytes + 1);

        Assert.NotNull(ClassFolderRemote.DecodeJson<RemoteState>(atTheLimit));
        Assert.Null(ClassFolderRemote.DecodeJson<RemoteState>(overTheLimit));
    }

    private static byte[] PaddedTo(int length)
    {
        var json = Bytes(ValidStateJson);
        var padded = new byte[length];
        json.CopyTo(padded, 0);
        Array.Fill(padded, (byte)' ', json.Length, length - json.Length);
        return padded;
    }

    // A valid command with `depth` arrays nested inside a field it doesn't know.
    private static byte[] CommandWithIgnoredNesting(int depth) => Bytes(
        "{\"kind\":\"start\",\"turma\":\"3º A\",\"later\":" + new string('[', depth) + new string(']', depth) + "}");

    [Fact]
    public void DecodeJson_ModestNestingInAnIgnoredField_IsAccepted()
    {
        // The control for the tests below: nothing but the depth is wrong with them.
        Assert.Equal(
            new RemoteCommand("start", "3º A", null, null),
            ClassFolderRemote.DecodeJson<RemoteCommand>(CommandWithIgnoredNesting(10)));
    }

    [Theory]
    [InlineData(40)]
    [InlineData(200)]
    public void DecodeJson_NestingTooDeepInAnIgnoredField_IsNull(int depth)
    {
        // 40 is under System.Text.Json's own default limit (64): the limit is ours.
        Assert.Null(ClassFolderRemote.DecodeJson<RemoteCommand>(CommandWithIgnoredNesting(depth)));
    }

    [Theory]
    [InlineData("\"text\"")]
    [InlineData("{ this is not json")]
    [InlineData("")]
    public void DecodeJson_ATypeThatIsNoDto_Throws(string json)
    {
        // A mistake in the code, not in what arrived: it never reads as a bad payload.
        Assert.Throws<NotSupportedException>(() => ClassFolderRemote.DecodeJson<string>(Bytes(json)));
        Assert.Throws<NotSupportedException>(() => ClassFolderRemote.DecodeJson<object>(Bytes(json)));
    }

    [Theory]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public void DecodeJson_AClassNameOverTheTurmaLimit_IsNull(int length, bool accepted)
    {
        var json = ValidStateJson.Replace(
            "\"classes\":[]", $"\"classes\":[{{\"turma\":\"{new string('a', length)}\",\"files\":0,\"bytes\":0}}]");

        Assert.Equal(accepted, ClassFolderRemote.DecodeJson<RemoteState>(Bytes(json)) is not null);
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void DecodeJson_APcNameOverTheLimit_IsNull(int length, bool accepted)
    {
        var json = ValidStateJson.Replace(
            "\"pcs\":[]", $"\"pcs\":[{{\"name\":\"{new string('a', length)}\",\"state\":\"InSync\",\"detail\":\"\"}}]");

        Assert.Equal(accepted, ClassFolderRemote.DecodeJson<RemoteState>(Bytes(json)) is not null);
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void DecodeJson_AResultNameOverTheLimit_IsNull(int length, bool accepted)
    {
        var json = $"{{\"ok\":true,\"detail\":\"\",\"lines\":[{{\"name\":\"{new string('a', length)}\",\"ok\":true,\"detail\":\"\"}}]}}";

        Assert.Equal(accepted, ClassFolderRemote.DecodeJson<RemoteReply>(Bytes(json)) is not null);
    }

    [Fact]
    public void DecodeJson_UnknownExtraFields_AreIgnored()
    {
        var command = ClassFolderRemote.DecodeJson<RemoteCommand>(Bytes("{\"kind\":\"create\",\"turma\":\"4º B\",\"later\":{\"x\":1}}"));

        Assert.Equal(new RemoteCommand("create", "4º B", null, null), command);
    }
}
