using Midview.Protocol;
using Midview.Session;
using Xunit;

// SessionServer: the class folder messages that change the student's files.
public partial class SessionServerTests
{
    [Fact]
    public async Task PushClassFile_SavesWithItsTime()
    {
        using var rig = new ClassFolderRig();
        var content = Enumerable.Range(0, FileTransfer.ChunkSize + 10).Select(i => (byte)(i * 7)).ToArray();
        var when = new DateTime(2026, 10, 20, 8, 30, 15, DateTimeKind.Utc);
        using var harness = new ServerHarness(new CountingScreenCapture(), classFolder: rig.Folder);
        using var teacher = harness.ConnectMaster();

        var (accepted, result) = await Task.Run(
            () => teacher.PushClassFile(rig.LessonId, "site/novo.bin", content, when.Ticks)).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal((true, string.Empty), accepted);
        Assert.Equal((true, string.Empty), result);
        var saved = rig.PathIn("site/novo.bin");
        Assert.Equal(content, File.ReadAllBytes(saved));
        Assert.Equal(when.Ticks, File.GetLastWriteTimeUtc(saved).Ticks);
        Assert.Equal(new[] { saved }, Directory.GetFiles(rig.LessonFolder, "*", SearchOption.AllDirectories)); // no temp file left
    }

    [Fact]
    public async Task PushClassFile_ReplacesAnOlderCopy_AndSavesAnEmptyFile()
    {
        using var rig = new ClassFolderRig();
        var older = WriteAndReturn(rig, "trabalho.txt", new byte[] { 1, 1, 1 });
        using var harness = new ServerHarness(new CountingScreenCapture(), classFolder: rig.Folder);
        var when = new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc).Ticks;

        using (var teacher = harness.ConnectMaster())
        {
            var (_, result) = await Task.Run(() => teacher.PushClassFile(rig.LessonId, "trabalho.txt", new byte[] { 2 }, when, VersionOf(older)))
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(result.Succeeded);
        }
        using (var teacher = harness.ConnectMaster())
        {
            var (_, result) = await Task.Run(() => teacher.PushClassFile(rig.LessonId, "vazio.txt", Array.Empty<byte>(), when))
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(result.Succeeded);
        }

        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(older));
        Assert.Equal(when, File.GetLastWriteTimeUtc(older).Ticks);
        Assert.Empty(File.ReadAllBytes(rig.PathIn("vazio.txt")));
    }

    // A push never lands on a file the Master didn't see there: the student may have just saved it.
    [Fact]
    public async Task PushClassFile_OntoAFileTheMasterDidNotSee_IsRefused_AndLeavesIt()
    {
        using var rig = new ClassFolderRig();
        var target = WriteAndReturn(rig, "trabalho.txt", "do aluno"u8.ToArray());
        var (size, ticks) = VersionOf(target);
        using var harness = new ServerHarness(new CountingScreenCapture(), classFolder: rig.Folder);

        foreach (var replacing in new (long, long)?[] { null, (size + 1, ticks) })
        {
            using var teacher = harness.ConnectMaster();
            var (accepted, _) = await Task.Run(
                () => teacher.PushClassFile(rig.LessonId, "trabalho.txt", new byte[] { 1 }, DateTime.UtcNow.Ticks, replacing))
                .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal((false, "mudou neste PC"), accepted);
        }

        Assert.Equal("do aluno", File.ReadAllText(target));
        Assert.Equal(new[] { target }, Directory.GetFiles(rig.LessonFolder));
    }

    [Fact]
    public async Task PushClassFile_ForAnotherLesson_IsRefused()
    {
        using var rig = new ClassFolderRig();
        var finished = Guid.NewGuid();
        Directory.CreateDirectory(rig.SetAsideFolder(finished));
        using var harness = new ServerHarness(new CountingScreenCapture(), classFolder: rig.Folder);

        // One never heard of, and one that has finished: neither takes files any more.
        foreach (var lessonId in new[] { Guid.NewGuid(), finished })
        {
            using var teacher = harness.ConnectMaster();
            var (accepted, _) = await Task.Run(
                () => teacher.PushClassFile(lessonId, "a.txt", new byte[] { 1 }, DateTime.UtcNow.Ticks)).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal((false, "aula diferente"), accepted);
        }

        Assert.Empty(Directory.GetFiles(rig.LessonFolder, "*", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(rig.SetAsideFolder(finished), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task PushClassFile_WhenTheLessonsFolderIsntThere_IsRefused_AndMakesNoFolder()
    {
        using var rig = new ClassFolderRig(lessonFolder: false);
        using var harness = new ServerHarness(new CountingScreenCapture(), classFolder: rig.Folder);
        using var teacher = harness.ConnectMaster();

        var (accepted, _) = await Task.Run(
            () => teacher.PushClassFile(rig.LessonId, "a.txt", new byte[] { 1 }, DateTime.UtcNow.Ticks)).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal((false, "sem pasta"), accepted);
        Assert.False(Directory.Exists(rig.LessonFolder));
    }

    [Fact]
    public async Task PushClassFile_OverAFileHeldOpen_ReportsArquivoEmUso()
    {
        using var rig = new ClassFolderRig();
        var target = WriteAndReturn(rig, "aberto.docx", "antigo"u8.ToArray());
        using var harness = new ServerHarness(new CountingScreenCapture(), classFolder: rig.Folder);
        using var teacher = harness.ConnectMaster();

        using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var (accepted, result) = await Task.Run(
                () => teacher.PushClassFile(rig.LessonId, "aberto.docx", "novo"u8.ToArray(), DateTime.UtcNow.Ticks, VersionOf(target)))
                .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(accepted.Succeeded);
            Assert.False(result.Succeeded);
            Assert.Contains("em uso", result.Detail);
        }

        Assert.Equal("antigo", File.ReadAllText(target));
        Assert.Equal(new[] { target }, Directory.GetFiles(rig.LessonFolder)); // the new copy was thrown away
    }

    [Fact]
    public async Task PushClassFile_ThatComesInCorrupt_IsNotSaved_AndSaysSo()
    {
        using var rig = new ClassFolderRig();
        using var harness = new ServerHarness(new CountingScreenCapture(), classFolder: rig.Folder);
        using var teacher = harness.ConnectMaster();
        teacher.Channel.Send(new Message(
            MessageType.PushClassFile, ClassFolderWire.EncodePushHeader(rig.LessonId, "a.txt", 10, DateTime.UtcNow.Ticks, null)));
        Assert.True(CommandResult.Decode(teacher.Channel.Receive().Payload).Succeeded);

        teacher.Channel.Send(new Message(MessageType.FileChunk, new byte[20])); // more than it said
        var reply = await Task.Run(() => teacher.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal((false, "a transferência chegou incompleta ou corrompida"), CommandResult.Decode(reply.Payload));
        Assert.Empty(Directory.GetFiles(rig.LessonFolder, "*", SearchOption.AllDirectories));
    }

    // Windows can't give a file a time before 1601, though the wire format can say one.
    [Fact]
    public async Task PushClassFile_WithATimeWindowsCantStore_IsAnsweredNotDropped()
    {
        using var rig = new ClassFolderRig();
        using var harness = new ServerHarness(new CountingScreenCapture(), classFolder: rig.Folder);
        using var teacher = harness.ConnectMaster();

        var (accepted, result) = await Task.Run(
            () => teacher.PushClassFile(rig.LessonId, "a.txt", new byte[] { 1 }, lastWriteTicks: 0)).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(accepted.Succeeded);
        Assert.False(result.Succeeded);
        Assert.StartsWith("não foi possível salvar o arquivo", result.Detail);
        Assert.Empty(Directory.GetFiles(rig.LessonFolder, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DeleteRenameClear_AnswerWithAResult()
    {
        using var rig = new ClassFolderRig();
        WriteFile(rig.LessonFolder, "apagar.txt", new byte[] { 1 });
        WriteFile(rig.LessonFolder, "velho.txt", new byte[] { 2 });
        WriteFile(rig.LessonFolder, "ocupado.txt", new byte[] { 3 });
        var finished = Guid.NewGuid();
        WriteFile(rig.SetAsideFolder(finished), "guardado.txt", new byte[] { 4 });
        var (size, ticks) = VersionOf(Path.Combine(rig.SetAsideFolder(finished), "guardado.txt"));
        var handled = new[] { new ClassFolderEntry(finished, "guardado.txt", size, ticks) };
        using var harness = new ServerHarness(new CountingScreenCapture(), classFolder: rig.Folder);

        (bool Succeeded, string Detail) Ask(MessageType type, byte[] payload)
        {
            using var teacher = harness.ConnectMaster();
            return teacher.Command(type, payload);
        }

        // Each message is a connection of its own.
        var deleted = await Task.Run(() => Ask(MessageType.DeleteClassFile, ClassFolderWire.EncodeFileRef(rig.LessonId, "apagar.txt")))
            .WaitAsync(TimeSpan.FromSeconds(10));
        var notThere = await Task.Run(() => Ask(MessageType.DeleteClassFile, ClassFolderWire.EncodeFileRef(rig.LessonId, "apagar.txt")))
            .WaitAsync(TimeSpan.FromSeconds(10));
        var renamed = await Task.Run(() => Ask(MessageType.RenameClassFile, ClassFolderWire.EncodeRename(rig.LessonId, "velho.txt", "novo.txt")))
            .WaitAsync(TimeSpan.FromSeconds(10));
        var taken = await Task.Run(() => Ask(MessageType.RenameClassFile, ClassFolderWire.EncodeRename(rig.LessonId, "ocupado.txt", "novo.txt")))
            .WaitAsync(TimeSpan.FromSeconds(10));
        var cleared = await Task.Run(() => Ask(MessageType.ClearSetAside, ClassFolderWire.EncodeClearSetAside(finished, handled)))
            .WaitAsync(TimeSpan.FromSeconds(10));
        var clearedAgain = await Task.Run(() => Ask(MessageType.ClearSetAside, ClassFolderWire.EncodeClearSetAside(finished, handled)))
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal((true, string.Empty), deleted);
        Assert.False(File.Exists(rig.PathIn("apagar.txt")));
        Assert.True(File.Exists(Path.Combine(rig.Bin.BinDirectory, "apagar.txt")));
        Assert.Equal((false, "arquivo não encontrado"), notThere);
        Assert.Equal((true, string.Empty), renamed);
        Assert.False(File.Exists(rig.PathIn("velho.txt")));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(rig.PathIn("novo.txt")));
        Assert.Equal((false, "já existe um arquivo com esse nome"), taken);
        Assert.True(File.Exists(rig.PathIn("ocupado.txt")));
        Assert.Equal((true, string.Empty), cleared);
        Assert.False(Directory.Exists(rig.SetAsideFolder(finished)));
        Assert.Equal((true, string.Empty), clearedAgain); // nothing set aside is nothing to clear
    }

    [Fact]
    public async Task ClassFolderMessages_AnswerAnInvalidRequest_WithoutTouchingAnything()
    {
        using var rig = new ClassFolderRig();
        WriteFile(rig.LessonFolder, "a.txt", new byte[] { 1 });
        using var harness = new ServerHarness(new CountingScreenCapture(), classFolder: rig.Folder);

        foreach (var type in new[]
                 {
                     MessageType.PullClassFile, MessageType.PushClassFile, MessageType.DeleteClassFile,
                     MessageType.RenameClassFile, MessageType.ClearSetAside,
                 })
        {
            using var teacher = harness.ConnectMaster();
            var result = await Task.Run(() => teacher.Command(type, new byte[] { 99, 1, 2 })).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.False(result.Succeeded);
            Assert.NotEmpty(result.Detail);
        }

        Assert.Equal(new[] { rig.PathIn("a.txt") }, Directory.GetFiles(rig.LessonFolder));
    }

    [Fact]
    public async Task ClassFolderMessages_WhileAnotherMasterIsControlling_StillRun()
    {
        using var rig = new ClassFolderRig();
        WriteFile(rig.LessonFolder, "a.txt", new byte[] { 7 });
        using var harness = new ServerHarness(new CountingScreenCapture(), classFolder: rig.Folder);
        using var controller = harness.ConnectMaster();
        controller.StartControl();
        await Task.Run(() => controller.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(5)); // it holds the control slot now

        using var teacher = harness.ConnectMaster();

        var (_, _, entries) = await Task.Run(teacher.ListClassFolder).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new[] { "a.txt" }, entries.Select(entry => entry.Path));
    }

    [Fact]
    public async Task WithoutClassFolder_AnswersEachWithARefusal()
    {
        using var harness = new ServerHarness(new CountingScreenCapture());
        var lessonId = Guid.NewGuid();
        var requests = new (MessageType Type, byte[] Payload)[]
        {
            (MessageType.ListClassFolder, Array.Empty<byte>()),
            (MessageType.PullClassFile, ClassFolderWire.EncodeFileRef(lessonId, "a.txt")),
            (MessageType.PushClassFile, ClassFolderWire.EncodePushHeader(lessonId, "a.txt", 1, DateTime.UtcNow.Ticks, null)),
            (MessageType.DeleteClassFile, ClassFolderWire.EncodeFileRef(lessonId, "a.txt")),
            (MessageType.RenameClassFile, ClassFolderWire.EncodeRename(lessonId, "a.txt", "b.txt")),
            (MessageType.ClearSetAside, ClassFolderWire.EncodeClearSetAside(lessonId, Array.Empty<ClassFolderEntry>())),
        };

        foreach (var (type, payload) in requests)
        {
            using var teacher = harness.ConnectMaster();
            var result = await Task.Run(() => teacher.Command(type, payload)).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal((false, "esta máquina não tem pasta da turma"), result);
        }
    }
}
