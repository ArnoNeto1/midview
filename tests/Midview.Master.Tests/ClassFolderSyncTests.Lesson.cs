using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderSync: ending a lesson, a lesson nobody ended, and the files PCs set
// aside for lessons that are over.
public sealed partial class ClassFolderSyncTests
{
    [Theory]
    [InlineData(EndChoice.Keep)]
    [InlineData(EndChoice.DeleteToday)]
    public async Task EndAsync_DeleteToday_And_Keep(EndChoice choice)
    {
        using var rig = new ClassSyncRig();
        // From an earlier lesson: it arrived on the day of its own time.
        rig.WriteInStore("antigo.pdf", Bytes(10, 1), new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));
        var sync = rig.NewSync();
        var lessons = new List<ClassLesson>();
        sync.LessonChanged += lessons.Add;
        await sync.StartAsync(Turma);
        var lesson = sync.Lesson;
        rig.Pcs[0].Write("hoje.txt", Bytes(10, 2), Utc(1));
        await rig.Rounds(sync, 2);

        // Saved in the last seconds, never listed before the end.
        rig.Pcs[1].Write("ultimo.txt", Bytes(10, 3), Utc(2));
        var ended = await sync.EndAsync(choice);

        Assert.All(ended, result => Assert.True(result.Ok, $"{result.Machine.Name}: {result.Detail}"));
        Assert.Equal(lesson with { SavedAt = sync.Lesson.SavedAt, Ended = true }, sync.Lesson);
        Assert.True(sync.Lesson.SavedAt > lesson.SavedAt); // the same second on the teacher's clock, still newer
        Assert.Equal(new[] { lesson, sync.Lesson }, lessons);
        Assert.Equal(sync.Lesson, ClassLessonFile.Read(rig.LessonPath));
        Assert.All(rig.Pcs, pc => Assert.Equal(sync.Lesson, pc.Agent.Current));
        Assert.True(rig.Store.IsCollected(lesson.LessonId));
        if (choice == EndChoice.Keep)
        {
            Assert.Equal(new[] { "antigo.pdf", "hoje.txt", "ultimo.txt" }, rig.StoreFiles());
            Assert.Empty(rig.TeacherBin.FileNames());
        }
        else
        {
            Assert.Equal(new[] { "antigo.pdf" }, rig.StoreFiles());
            Assert.Equal(new[] { "hoje.txt", "ultimo.txt" }, rig.TeacherBin.FileNames());
        }
    }

    [Fact]
    public async Task SetAsideFiles_OfAnUncollectedLesson_ArePulledIntoTheirClass_ThenCleared()
    {
        using var rig = new ClassSyncRig();
        var yesterday = rig.NewSync();
        await yesterday.StartAsync(Turma);
        var lessonA = yesterday.Lesson;
        var pc1 = rig.Pcs[0];
        pc1.Write("velho.txt", Bytes(10, 1), Utc(1));
        await rig.Rounds(yesterday, 2);
        pc1.Follow();
        // Saved after the last round; then everybody went home.
        pc1.Write("novo.txt", Bytes(10, 2), Utc(2));
        yesterday.Dispose();

        // The next morning each Agent ends the lesson by itself, and the Session sets its files aside.
        rig.Now = rig.Now.AddDays(1).Date.AddHours(8);
        foreach (var pc in rig.Pcs)
        {
            pc.Agent.Reconcile();
            pc.Follow();
        }
        Assert.False(pc1.Agent.Current.IsOn);

        var today = rig.NewSync();
        Assert.Equal(lessonA.LessonId, today.Lesson.LessonId);
        Assert.True(today.Lesson.Ended);
        await today.StartAsync("3º B");
        await today.RunRoundAsync(pc1.Machine, CancellationToken.None);

        Assert.Equal(new[] { "novo.txt", "velho.txt" }, rig.StoreFiles(Turma)); // no "(2)" of what the class had
        Assert.Equal(Bytes(10, 2), File.ReadAllBytes(rig.StorePath("novo.txt")));
        Assert.Empty(rig.StoreFiles("3º B"));
        var (current, _, entries) = pc1.Folder.List();
        Assert.Equal(today.Lesson.LessonId, current);
        Assert.Empty(entries); // the set-aside folder went to the student's Recycle Bin
    }

    [Fact]
    public async Task SetAsideFiles_OfALessonNobodyEnded_BringBackOnlyWhatIsNew()
    {
        using var rig = new ClassSyncRig();
        rig.WriteInStore("apagado.txt", Bytes(10, 1), Utc(1));
        rig.WriteInStore("fica.txt", Bytes(10, 2), Utc(1));
        var yesterday = rig.NewSync();
        await yesterday.StartAsync(Turma);
        var lessonA = yesterday.Lesson;
        var pc1 = rig.Pcs[0];
        await yesterday.RunRoundAsync(pc1.Machine, CancellationToken.None);
        pc1.Follow();
        Assert.True(pc1.Has("apagado.txt"));

        // The teacher deletes a file and PC1 never hears of it; its student saves a new one.
        File.Delete(rig.StorePath("apagado.txt"));
        pc1.Write("novo.txt", Bytes(10, 3), Utc(2));
        yesterday.Dispose();

        rig.Now = rig.Now.AddDays(1).Date.AddHours(8);
        pc1.Agent.Reconcile();
        pc1.Follow();
        var today = rig.NewSync();
        await today.StartAsync("3º B");
        await today.RunRoundAsync(pc1.Machine, CancellationToken.None);

        Assert.Equal(new[] { "fica.txt", "novo.txt" }, rig.StoreFiles(Turma));
        Assert.Equal(Bytes(10, 3), File.ReadAllBytes(rig.StorePath("novo.txt")));
        Assert.DoesNotContain(pc1.Folder.List().Entries, entry => entry.LessonId == lessonA.LessonId);
        Assert.False(rig.Store.IsCollected(lessonA.LessonId));
    }

    [Fact]
    public async Task SetAsideFiles_TheSameOnEveryPc_ComeInOnce()
    {
        using var rig = new ClassSyncRig();
        var yesterday = rig.NewSync();
        await yesterday.StartAsync(Turma);
        var lessonA = yesterday.Lesson;
        foreach (var pc in rig.Pcs)
        {
            pc.Follow();
            pc.Write("igual.txt", Bytes(10, 1), Utc(1)); // the same file everywhere, never copied
        }
        yesterday.Dispose();
        // The class has another file of that name by now.
        rig.WriteInStore("igual.txt", Bytes(20, 2), Utc(2));

        rig.Now = rig.Now.AddDays(1).Date.AddHours(8);
        foreach (var pc in rig.Pcs)
        {
            pc.Agent.Reconcile();
            pc.Follow();
        }
        var today = rig.NewSync();
        await today.StartAsync("3º B");
        await Task.WhenAll(rig.Pcs.Select(pc => today.RunRoundAsync(pc.Machine, CancellationToken.None)));
        await rig.Rounds(today, 1);

        Assert.Equal(new[] { "igual (2).txt", "igual.txt" }, rig.StoreFiles(Turma));
        Assert.Equal(Bytes(10, 1), File.ReadAllBytes(rig.StorePath("igual (2).txt")));
        Assert.Equal(Bytes(20, 2), File.ReadAllBytes(rig.StorePath("igual.txt")));
        Assert.All(rig.Pcs, pc => Assert.DoesNotContain(pc.Folder.List().Entries, entry => entry.LessonId == lessonA.LessonId));
    }

    [Fact]
    public async Task SetAsideFiles_OfACollectedLesson_AreOnlyCleared()
    {
        using var rig = new ClassSyncRig();
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        var lessonA = sync.Lesson;
        foreach (var pc in rig.Pcs)
        {
            pc.Follow();
        }
        var pc3 = rig.Pcs[2];
        rig.Offline[pc3.Name] = true;
        pc3.Write("offline.txt", Bytes(10, 1), Utc(1));

        await sync.EndAsync(EndChoice.DeleteAll);
        pc3.Follow();
        await sync.StartAsync("3º B");
        rig.Offline.TryRemove(pc3.Name, out _);
        await sync.RunRoundAsync(pc3.Machine, CancellationToken.None);

        Assert.True(rig.Store.IsCollected(lessonA.LessonId));
        Assert.Empty(rig.StoreFiles(Turma));
        Assert.Empty(rig.StoreFiles("3º B"));
        Assert.DoesNotContain(pc3.Folder.List().Entries, entry => entry.LessonId == lessonA.LessonId);
        Assert.Contains("offline.txt", pc3.Bin.FileNames()); // its own Recycle Bin, never the class
    }

    [Fact]
    public void ALessonFromYesterday_IsEndedKeepingFilesOnStart()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        rig.WriteInStore("mapa.png", Bytes(10, 1), Utc(1));
        var started = rig.Now.AddDays(-1);
        var lesson = new ClassLesson(1000, Guid.NewGuid(), Turma, new DateTimeOffset(started).ToUnixTimeSeconds(), Ended: false);
        ClassLessonFile.Write(rig.LessonPath, lesson);
        rig.Store.RememberLesson(lesson.LessonId, Turma);

        var sync = rig.NewSync();

        Assert.Equal(lesson.LessonId, sync.Lesson.LessonId);
        Assert.True(sync.Lesson.Ended);
        Assert.True(sync.Lesson.SavedAt > lesson.SavedAt);
        Assert.Equal(sync.Lesson, ClassLessonFile.Read(rig.LessonPath));
        Assert.Equal(new[] { "mapa.png" }, rig.StoreFiles());
        Assert.False(rig.Store.IsCollected(lesson.LessonId)); // what PCs set aside still comes in
        Assert.Equal(Turma, rig.Store.TurmaOfLesson(lesson.LessonId));
    }

    [Fact]
    public void ALessonFromToday_CarriesOn()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        var lesson = new ClassLesson(1000, Guid.NewGuid(), Turma, new DateTimeOffset(rig.Now.AddHours(-1)).ToUnixTimeSeconds(), Ended: false);
        ClassLessonFile.Write(rig.LessonPath, lesson);

        var sync = rig.NewSync();

        Assert.Equal(lesson, sync.Lesson);
    }

    // A PC that was off when the teacher chose "Apagar tudo" goes straight from that lesson to the next
    // one of the same class, in the same Desktop folder: what it still holds is the ended lesson's, set
    // aside and cleared (that lesson was collected), never the new lesson's new files.
    [Fact]
    public async Task APcOfflineAtTheEnd_TheNextLessonOfTheSameClass_BringsNothingDeletedBack()
    {
        using var rig = new ClassSyncRig();
        rig.WriteInStore("mapa.png", Bytes(10, 1), Utc(1));
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        var first = sync.Lesson;
        foreach (var pc in rig.Pcs)
        {
            pc.Follow();
        }
        await rig.Rounds(sync, 1);
        var pc3 = rig.Pcs[2];
        Assert.True(pc3.Has("mapa.png"));
        rig.Offline[pc3.Name] = true;
        pc3.Write("offline.txt", Bytes(10, 2), Utc(2));

        // The other PCs see the end; PC3's Session sees nothing until the next lesson is on.
        await sync.EndAsync(EndChoice.DeleteAll);
        rig.Pcs[0].Follow();
        rig.Pcs[1].Follow();
        await sync.StartAsync(Turma);
        rig.Offline.TryRemove(pc3.Name, out _);
        foreach (var pc in rig.Pcs)
        {
            pc.Follow();
        }
        await rig.Rounds(sync, 3);

        Assert.True(rig.Store.IsCollected(first.LessonId));
        Assert.Empty(rig.StoreFiles());
        Assert.Empty(pc3.Files());
        Assert.DoesNotContain(pc3.Folder.List().Entries, entry => entry.LessonId == first.LessonId);
        Assert.Equal(new[] { "mapa.png", "offline.txt" }, pc3.Bin.FileNames()); // its own Recycle Bin, never the class
    }

    // Not something the window does (it ends a lesson before showing the classes), but a start while a
    // lesson is on ends that lesson first, keeping the files and remembering every version known of
    // them, whatever the class: what the PCs then set aside of it brings in only what was never seen.
    [Fact]
    public async Task StartAsync_WhileALessonIsOn_EndsItFirst_KeepingTheFiles()
    {
        using var rig = new ClassSyncRig();
        rig.WriteInStore("mapa.png", Bytes(10, 1), Utc(1));
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        var first = sync.Lesson;
        foreach (var pc in rig.Pcs)
        {
            pc.Follow();
        }
        await rig.Rounds(sync, 1);
        var pc1 = rig.Pcs[0];
        pc1.Write("novo.txt", Bytes(10, 2), Utc(2)); // never copied

        var again = await sync.StartAsync(Turma);
        foreach (var pc in rig.Pcs)
        {
            pc.Follow();
        }
        await rig.Rounds(sync, 2);

        Assert.All(again, result => Assert.True(result.Ok, $"{result.Machine.Name}: {result.Detail}"));
        Assert.NotEqual(first.LessonId, sync.Lesson.LessonId);
        Assert.False(rig.Store.IsCollected(first.LessonId));
        Assert.True(rig.Store.IsKnownVersion(first.LessonId, "mapa.png", new FileVersion(10, Utc(1).Ticks)));
        Assert.Equal(new[] { "mapa.png", "novo.txt" }, rig.StoreFiles()); // no "(2)" of what the class had
        Assert.All(rig.Pcs, pc => Assert.Equal(new[] { "mapa.png", "novo.txt" }, pc.Files()));

        // Another class: every Agent follows it, and the first class keeps its files.
        var changed = await sync.StartAsync("3º B");

        Assert.All(changed, result => Assert.True(result.Ok, $"{result.Machine.Name}: {result.Detail}"));
        Assert.All(rig.Pcs, pc => Assert.Equal(sync.Lesson, pc.Agent.Current));
        Assert.Equal("3º B", sync.Lesson.Turma);
        Assert.Equal(new[] { "mapa.png", "novo.txt" }, rig.StoreFiles());
    }

    [Fact]
    public async Task StartAsync_AClassThatIsNoMore_IsRefused()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        var sync = rig.NewSync();

        await Assert.ThrowsAsync<ArgumentException>(() => sync.StartAsync("não existe"));
        Assert.False(sync.Lesson.IsOn);
    }

    // A Session from before the class folder: it takes any request for a control
    // connection and answers with a screen tile.
    private sealed class OldSession : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly X509Certificate2 _certificate = SelfSignedCertificate.Create("old-session");

        public OldSession(X509Certificate2 master)
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            var masters = new TrustStore(new[] { master.Thumbprint });
            Task.Run(() =>
            {
                while (true)
                {
                    TcpClient client;
                    try
                    {
                        client = _listener.AcceptTcpClient();
                    }
                    catch (Exception)
                    {
                        return; // Dispose stopped the listener
                    }

                    using (client)
                    {
                        try
                        {
                            using var channel = SecureChannel.AuthenticateAsServer(client, _certificate, masters);
                            channel.Receive();
                            channel.Send(new Message(MessageType.FrameTile, new byte[] { 1, 2, 3 }));
                        }
                        catch (Exception)
                        {
                            // The other side gave up.
                        }
                    }
                }
            });
        }

        public int Port { get; }

        public string Thumbprint => _certificate.Thumbprint;

        public void Dispose()
        {
            _listener.Stop();
            _certificate.Dispose();
        }
    }
}
