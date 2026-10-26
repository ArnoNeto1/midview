using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderRole: how a Master works the "Pasta da turma" by the room's word
// (Task 7). Whole teacher PCs over loopback (TeacherPc: store, sync, local
// controller, hand-over, tracker, role, and a host on a port of its own),
// against the rig's real Agents. Temp roots only; no lab machine.
public sealed partial class ClassFolderRoleTests
{
    private const string Turma = ClassSyncRig.Turma;
    private const string Prof1 = ClassSyncRig.MachineName;
    private const string Prof2 = "PROF-2";

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    // A single teacher PC: Local at its first reading, its host open (unused);
    // no lesson goes to the pollers until a reading that asked an Agent, which
    // a machine answering for the first time asks for.
    [Fact]
    public async Task OneTeacherPc_IsLocalAtOnce_AndHandsLessonsOutOnceAnAgentWasAsked()
    {
        using var rig = new ClassSyncRig(homes: 3);
        foreach (var pc in rig.Pcs)
        {
            rig.Offline[pc.Name] = true;
        }
        using var prof1 = new TeacherPc(rig, Prof1, rig.Master);

        await prof1.Role.RefreshAndActAsync();

        Assert.Equal(ClassFolderMode.Local, prof1.Role.Mode);
        Assert.Same(prof1.Local, prof1.Role.Controller);
        Assert.NotNull(prof1.Role.Host);
        Assert.True(Listens(prof1.HostPort));
        Assert.Null(prof1.Role.LessonForPollers);
        Assert.Null(prof1.Role.HomeGeneration);

        rig.Offline.Clear();
        prof1.Role.MachineAnswered();
        await Until(() => prof1.Role.LessonForPollers is not null);
        Assert.Equal(prof1.Sync.Lesson, prof1.Role.LessonForPollers);
        Assert.Null(prof1.Role.HomeGeneration); // no PC is the home yet

        await prof1.Sync.StartAsync(Turma);
        await prof1.Role.RefreshAndActAsync();

        Assert.Equal(prof1.Sync.Lesson, prof1.Role.LessonForPollers);
        Assert.Equal(prof1.Sync.Lesson, prof1.Role.ShownLesson);
        Assert.Equal(rig.NowTicks, prof1.Role.HomeGeneration);
        Assert.Equal(ClassFolderMode.Local, prof1.Role.Mode);
    }

    // Another PC is the home: Remote, but only once a second reading says so.
    [Fact]
    public async Task AnotherPcAsTheHome_MakesThisOneRemote_OnlyAtASecondReading()
    {
        using var rig = new ClassSyncRig(homes: 3);
        ClaimEveryAgent(rig, rig.OtherMaster, Prof2, generation: 1_000);
        using var prof1 = new TeacherPc(rig, Prof1, rig.Master);
        var changes = 0;
        prof1.Role.Changed += () => Interlocked.Increment(ref changes);

        await prof1.Role.RefreshAndActAsync();

        Assert.Null(prof1.Role.Mode); // not decided yet: as a Master that just opened
        Assert.Same(prof1.Local, prof1.Role.Controller);
        Assert.Null(prof1.Role.Host);
        Assert.Null(prof1.Role.LessonForPollers);

        await prof1.Role.RefreshAndActAsync();

        Assert.Equal(ClassFolderMode.Remote, prof1.Role.Mode);
        var remote = Assert.IsType<RemoteClassFolderController>(prof1.Role.Controller);
        Assert.Equal(Prof2, remote.HomeName);
        Assert.Null(prof1.Role.Host);
        Assert.False(Listens(prof1.HostPort));
        Assert.Null(prof1.Role.LessonForPollers);
        Assert.Null(prof1.Role.HomeGeneration);
        Assert.True(Volatile.Read(ref changes) > 0);
    }

    // One reading against the mode acted on (class-home.json unreadable for a
    // moment, say) changes nothing: the host stays, this PC doesn't stand down
    // (its ended lesson stays, for the pollers to hand PCs that were off), and
    // only the pollers wait meanwhile.
    [Fact]
    public async Task OneReadingAgainst_ChangesNothing()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var prof1 = new TeacherPc(rig, Prof1, rig.Master);
        await prof1.Sync.StartAsync(Turma);
        await prof1.Sync.EndAsync(EndChoice.Keep);
        await prof1.Role.RefreshAndActAsync();
        var lesson = prof1.Sync.Lesson;
        Assert.Equal(lesson, prof1.Role.LessonForPollers);
        var other = new ClassHome(rig.OtherMaster.Thumbprint, Prof2, "127.0.0.1", 1, rig.NowTicks + 1);

        await prof1.Role.ActOnAsync(new ClassHomeReading(other, ClassFolderMode.Remote, 3));

        Assert.Equal(ClassFolderMode.Local, prof1.Role.Mode);
        Assert.Same(prof1.Local, prof1.Role.Controller);
        Assert.True(Listens(prof1.HostPort));
        Assert.Equal(lesson, prof1.Sync.Lesson);
        Assert.Null(prof1.Role.LessonForPollers);

        await prof1.Role.RefreshAndActAsync();

        Assert.Equal(lesson, prof1.Role.LessonForPollers);
        Assert.Equal(ClassFolderMode.Local, prof1.Role.Mode);
    }

    // Ruling 17: a PC whose lesson the room still follows keeps the folders (and
    // its own window, to end that lesson) until it ends; then it stands down.
    // Meanwhile the lesson is still handed out (Fix round 1, minor 3: the
    // Agents enforce the home on lesson sets), and once it ends its end goes on
    // being handed out for EndedGrace, to the PCs that were busy or off.
    [Fact]
    public async Task ALessonOfThisPcTheRoomFollows_KeepsItLocal_UntilItEnds()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var prof1 = new TeacherPc(rig, Prof1, rig.Master, endedGrace: TimeSpan.FromSeconds(2));
        await prof1.Sync.StartAsync(Turma);
        await prof1.Sync.EndAsync(EndChoice.Keep);
        // PC1 took another PC's take-over, and was off for this PC's start (its
        // Agent busy here: the rig's offline PCs still answer a lesson set, and
        // one refused for a newer home would take the start back): it doesn't
        // follow this PC's lesson, the other two do.
        Claim(rig, rig.Pcs[0], rig.OtherMaster, Prof2, rig.NowTicks + TimeSpan.TicksPerHour, takeOver: true);
        rig.Offline[rig.Pcs[0].Name] = true;
        using (new BusyAgents(new[] { rig.Pcs[0] }))
        {
            await prof1.Sync.StartAsync("3º B");
        }
        rig.Offline.Clear();
        Assert.False(rig.Pcs[0].Agent.Current.IsOn);
        Assert.True(rig.Pcs[1].Agent.Current.IsOn);

        await prof1.Role.RefreshAndActAsync(); // Remote, once: nothing decided yet
        Assert.Null(prof1.Role.Mode);
        await prof1.Role.RefreshAndActAsync(); // Remote, confirmed: but the lesson is on, so Local
        await prof1.Role.RefreshAndActAsync();

        Assert.Equal(ClassFolderMode.Local, prof1.Role.Mode);
        Assert.Same(prof1.Local, prof1.Role.Controller);
        Assert.True(prof1.Sync.Lesson.IsOn);
        Assert.Equal(prof1.Sync.Lesson, prof1.Role.LessonForPollers);
        Assert.Null(prof1.Role.HomeGeneration); // no straggler is taken back meanwhile

        await prof1.Sync.EndAsync(EndChoice.Keep);
        var ended = prof1.Sync.Lesson;
        await prof1.Role.RefreshAndActAsync();

        Assert.Equal(ClassFolderMode.Local, prof1.Role.Mode);
        Assert.Equal(ended, prof1.Role.LessonForPollers);
        Assert.True(Listens(prof1.HostPort));

        await Until(() => prof1.Role.Mode == ClassFolderMode.Remote); // the grace over, by itself
        Assert.IsType<RemoteClassFolderController>(prof1.Role.Controller);
        Assert.False(Listens(prof1.HostPort));
        Assert.Equal(ClassLesson.None(0), prof1.Sync.Lesson);
        Assert.Null(prof1.Role.LessonForPollers);
    }

    // The toolbar's lesson: this PC's when Local; the home's when Remote, as the
    // room's Agents follow it (no window needs to be open).
    [Fact]
    public async Task TheLessonShown_IsTheHomesWhenRemote()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var home = rig.NewSync(); // PROF-1, the home, with no role of its own here
        await home.StartAsync(Turma);
        await home.EndAsync(EndChoice.Keep);
        using var prof2 = new TeacherPc(rig, Prof2, rig.OtherMaster);
        await prof2.Role.RefreshAndActAsync();
        await prof2.Role.RefreshAndActAsync();
        Assert.Equal(ClassFolderMode.Remote, prof2.Role.Mode);
        Assert.False(prof2.Role.ShownLesson.IsOn);

        await home.StartAsync("3º B");
        await prof2.Role.RefreshAndActAsync();

        Assert.Equal(home.Lesson, prof2.Role.ShownLesson);
        Assert.Equal(ClassLesson.None(0), prof2.Sync.Lesson);
    }

    // A take-over moves both PCs at once, each by itself, from the hand-over
    // telling its role: no second reading is waited for (each one's would come
    // in an hour here). The new home goes Local (the Agents name it; its host
    // opens, its window works its own store), and the old one Remote (it
    // handed the folders over itself; its host closes).
    [Fact]
    public async Task ATakeOver_TurnsTheNewHomeLocal_AndTheOldOneRemote()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var prof1 = new TeacherPc(rig, Prof1, rig.Master);
        using var prof2 = new TeacherPc(rig, Prof2, rig.OtherMaster);
        await prof1.Sync.StartAsync(Turma);
        await prof1.Sync.EndAsync(EndChoice.Keep);
        await prof1.Role.RefreshAndActAsync();
        await prof2.Role.RefreshAndActAsync();
        await prof2.Role.RefreshAndActAsync();
        Assert.Equal(ClassFolderMode.Local, prof1.Role.Mode);
        var remote = Assert.IsType<RemoteClassFolderController>(prof2.Role.Controller);
        Assert.Equal(new[] { Turma, "3º B" }, (await remote.ReadAsync(lessonFiles: false)).Classes.Select(c => c.Turma));

        var result = await remote.TakeOverAsync(prof2.HandOver, remote.Home, homeClosed: false);

        Assert.True(result.Ok, result.Detail);
        await Until(() => prof2.Role.Mode == ClassFolderMode.Local && prof1.Role.Mode == ClassFolderMode.Remote);
        Assert.Same(prof2.Local, prof2.Role.Controller);
        Assert.True(Listens(prof2.HostPort));
        Assert.False(Listens(prof1.HostPort));
        var back = Assert.IsType<RemoteClassFolderController>(prof1.Role.Controller);
        Assert.Equal(Prof2, back.HomeName);
        Assert.Equal(new[] { Turma, "3º B" }, (await back.ReadAsync(lessonFiles: false)).Classes.Select(c => c.Turma));

        // The remote PROF-2 worked through is kept (a window may hold it) until released.
        Assert.IsNotType<ObjectDisposedException>(await Record.ExceptionAsync(() => remote.DeleteAsync("Nenhuma")));
        prof2.Role.ReleaseRetired();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => remote.DeleteAsync("Nenhuma"));
    }

    // Is anything listening there?
    private static bool Listens(int port)
    {
        try
        {
            using var tcp = new TcpClient();
            tcp.Connect(IPAddress.Loopback, port);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static void ClaimEveryAgent(ClassSyncRig rig, X509Certificate2 master, string name, long generation, bool takeOver = false)
    {
        foreach (var pc in rig.Pcs)
        {
            Claim(rig, pc, master, name, generation, takeOver);
        }
    }

    private static void Claim(
        ClassSyncRig rig, StudentPc pc, X509Certificate2 master, string name, long generation, bool takeOver = false)
    {
        var (ok, detail, _) = ClassFolderClient.ClaimHome(
            pc.Machine, master, new ClassHomeClaim(name, ClassFolderRemote.Port, takeOver, generation), rig.AgentPort);
        Assert.True(ok, detail);
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out");
            await Task.Delay(50);
        }
    }
}
