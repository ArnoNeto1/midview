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

    [Fact]
    public async Task StartAsync_WhileALessonIsOn_TheSameClassGoesOn_AnotherTakesOver()
    {
        using var rig = new ClassSyncRig();
        rig.WriteInStore("mapa.png", Bytes(10, 1), Utc(1));
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        var first = sync.Lesson;
        await rig.Rounds(sync, 1);
        var pc1 = rig.Pcs[0];
        pc1.Follow();
        pc1.Write("mapa.png", Bytes(20, 2), Utc(3)); // an edit, not copied yet

        // The same class once more: the PCs keep their folder, and the edit is still an edit, not a "(2)".
        var again = await sync.StartAsync(Turma);
        pc1.Follow();
        await rig.Rounds(sync, 2);

        Assert.All(again, result => Assert.True(result.Ok, $"{result.Machine.Name}: {result.Detail}"));
        Assert.NotEqual(first.LessonId, sync.Lesson.LessonId);
        Assert.Equal(new[] { "mapa.png" }, rig.StoreFiles());
        Assert.Equal(Bytes(20, 2), File.ReadAllBytes(rig.StorePath("mapa.png")));
        Assert.All(rig.Pcs, pc => Assert.Equal(Bytes(20, 2), pc.Read("mapa.png")));

        // Another class: every Agent follows it, and the first class keeps its files.
        var changed = await sync.StartAsync("3º B");

        Assert.All(changed, result => Assert.True(result.Ok, $"{result.Machine.Name}: {result.Detail}"));
        Assert.All(rig.Pcs, pc => Assert.Equal(sync.Lesson, pc.Agent.Current));
        Assert.Equal("3º B", sync.Lesson.Turma);
        Assert.Equal(new[] { "mapa.png" }, rig.StoreFiles());
        Assert.False(rig.Store.IsCollected(first.LessonId));
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
