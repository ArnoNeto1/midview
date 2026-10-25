using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// RemoteClassFolderController: a colleague's teacher PC working the home's
// "Pasta da turma" window (plan decisions 6 and 9, Review Focus 4). Two
// Masters over loopback: the home is the rig's Master (PROF-1) with its real
// store, sync, LocalClassFolderController and ClassFolderHost; the colleague is
// the rig's OtherMaster with a RemoteClassFolderController over the same
// classroom (the rig's Agents trust both). A scripted home controller checks
// what the remote does with what a home answers. Loopback only, temp roots,
// ports the system picks; the polling loop runs only where a test starts it.
public sealed partial class RemoteClassFolderControllerTests
{
    private const string Turma = ClassSyncRig.Turma;

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    private static readonly string[] AllPcs = { "PC1", "PC2", "PC3" };

    [Fact]
    public async Task TheRemote_SeesTheHomesClassesLessonAndPcs()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new TwoMasters(rig);

        var before = await lab.Remote.ReadAsync(lessonFiles: false);

        Assert.Equal(ClassSyncRig.MachineName, lab.Remote.HomeName);
        Assert.False(lab.Remote.HomeClosed);
        Assert.Null(lab.Remote.Note);
        Assert.Equal(new[] { Turma, "3º B" }, before.Classes.Select(summary => summary.Turma));
        Assert.False(lab.Remote.Lesson.IsOn);
        Assert.Equal(AllPcs, lab.Remote.Pcs.Select(pc => pc.Name));

        await lab.Home.StartAsync(Turma);
        var changes = 0;
        lab.Remote.Changed += () => Interlocked.Increment(ref changes);
        await lab.Remote.PollAsync();

        Assert.True(changes > 0);
        Assert.Equal(lab.Home.Lesson, lab.Remote.Lesson);
        Assert.Equal(lab.Home.Pcs, lab.Remote.Pcs);
        var during = await lab.Remote.ReadAsync(lessonFiles: true);
        Assert.Equal(Turma, during.LessonFiles?.Turma);
    }

    [Fact]
    public async Task TheRemote_StartsAndEndsALesson_ThatTheHomesWindowShows()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new TwoMasters(rig);

        var started = await lab.Remote.StartAsync(Turma);

        Assert.Equal(AllPcs, started.Select(line => line.Name));
        Assert.All(started, line => Assert.True(line.Ok, $"{line.Name}: {line.Detail}"));
        Assert.True(lab.Home.Lesson.IsOn);
        Assert.Equal(Turma, lab.Home.Lesson.Turma);
        // Read again before StartAsync returned: the window looks at Lesson right after it.
        Assert.Equal(lab.Home.Lesson, lab.Remote.Lesson);
        var lessonId = lab.Home.Lesson.LessonId;

        var ended = await lab.Remote.EndAsync(Turma, EndChoice.Keep);

        Assert.Equal(AllPcs, ended.Select(line => line.Name));
        Assert.All(ended, line => Assert.True(line.Ok, $"{line.Name}: {line.Detail}"));
        Assert.False(lab.Home.Lesson.IsOn);
        Assert.Equal(lessonId, lab.Home.Lesson.LessonId);
        Assert.Equal(lab.Home.Lesson, lab.Remote.Lesson);
    }

    [Fact]
    public async Task TheRemote_CreatesRenamesAndDeletes_AndGetsTheHomesRefusals()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new TwoMasters(rig);

        Assert.Equal((true, string.Empty, "Turma 1-2"), await lab.Remote.CreateAsync("Turma 1/2"));
        Assert.True(rig.Store.Exists("Turma 1-2"));
        var taken = await lab.Remote.CreateAsync("Turma 1-2");
        Assert.False(taken.Ok);
        Assert.NotEmpty(taken.Detail);
        Assert.Equal(string.Empty, taken.Turma);
        Assert.Equal((true, string.Empty), await lab.Remote.RenameAsync("Turma 1-2", "9º A"));
        Assert.True(rig.Store.Exists("9º A"));
        Assert.Equal((true, string.Empty), await lab.Remote.DeleteAsync("9º A"));
        Assert.False(rig.Store.Exists("9º A"));

        await lab.Home.StartAsync(Turma);
        var second = await Assert.ThrowsAsync<ClassFolderRefusedException>(() => lab.Remote.StartAsync("3º B"));
        Assert.Equal(ClassFolderText.LessonAlreadyOn(Turma), second.Message);
        var other = await Assert.ThrowsAsync<ClassFolderRefusedException>(() => lab.Remote.EndAsync("3º B", EndChoice.Keep));
        Assert.Equal(ClassFolderText.LessonAlreadyOver("3º B"), other.Message);
        Assert.Equal((false, ClassFolderText.LessonClassNotDeleted), await lab.Remote.DeleteAsync(Turma));
        Assert.Equal(Turma, lab.Home.Lesson.Turma);
        Assert.True(lab.Home.Lesson.IsOn);
    }

    // The home's per-address cap closes a connection over it during the
    // handshake: the home is there, only busy, and is never taken for closed.
    [Fact]
    public async Task ConnectionsTheHomeTurnsAway_NeverMakeItClosed()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new TwoMasters(rig);
        await lab.Remote.PollAsync();
        lab.Host.ReadTimeout = Wait;
        var held = Enumerable.Range(0, 3).Select(_ =>
        {
            var tcp = new TcpClient();
            tcp.Connect(IPAddress.Loopback, lab.Host.Port);
            return tcp;
        }).ToList();
        try
        {
            for (var poll = 0; poll < 5; poll++)
            {
                await lab.Remote.PollAsync();
                Assert.False(lab.Remote.HomeClosed);
            }
            await lab.Home.StartAsync(Turma);
            await lab.Remote.PollAsync();
            Assert.False(lab.Remote.Lesson.IsOn); // never got through, so the last view stays
        }
        finally
        {
            held.ForEach(tcp => tcp.Dispose());
        }

        await Until(async () =>
        {
            await lab.Remote.PollAsync();
            return lab.Remote.Lesson == lab.Home.Lesson;
        });
        Assert.False(lab.Remote.HomeClosed);
    }

    // One poll that finds nobody is not enough: a PC that came back up, a
    // dropped packet. Several in a row are; and the home is back as soon as it answers.
    [Fact]
    public async Task TheHomeIsClosed_OnlyOnceSeveralPollsInARowFindNobody_AndBackWhenItAnswers()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new TwoMasters(rig);
        await lab.Remote.PollAsync();
        lab.Host.Dispose();
        var changes = 0;
        lab.Remote.Changed += () => Interlocked.Increment(ref changes);

        for (var poll = 1; poll < RemoteClassFolderController.ClosedAfter; poll++)
        {
            await lab.Remote.PollAsync();
            Assert.False(lab.Remote.HomeClosed);
        }
        Assert.Equal(0, changes);
        await lab.Remote.PollAsync();

        Assert.True(lab.Remote.HomeClosed);
        Assert.Equal(1, changes);
        Assert.Equal(ClassSyncRig.MachineName, lab.Remote.HomeName);
        await Assert.ThrowsAnyAsync<IOException>(() => lab.Remote.ReadAsync(lessonFiles: false));

        lab.ReopenHost();
        await lab.Remote.PollAsync();

        Assert.False(lab.Remote.HomeClosed);
        Assert.Equal(2, changes);
    }

    // Review Focus 4: the home's Master is closed (or the home's firewall keeps
    // this PC out). The remote shows the lesson the Agents follow, and only
    // "Encerrar a aula", keeping the files, works: straight on the Agents. The
    // home's Master, opened again, takes that end as one nobody collected, so
    // what the PCs set aside comes in at its next lesson.
    [Fact]
    public async Task WithTheHomeClosed_TheRemoteEndsTheLessonOnTheAgents_AndTheHomeOpenedAgainAdoptsIt()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new TwoMasters(rig);
        await lab.Remote.StartAsync(Turma);
        var lesson = lab.Home.Lesson;
        foreach (var pc in rig.Pcs)
        {
            pc.Follow();
        }
        rig.Pcs[0].Write("depois.txt", new byte[] { 1, 2, 3 }, new DateTime(2026, 10, 20, 13, 0, 0, DateTimeKind.Utc));

        lab.CloseHome();
        for (var poll = 0; poll < RemoteClassFolderController.ClosedAfter; poll++)
        {
            await lab.Remote.PollAsync();
        }

        Assert.True(lab.Remote.HomeClosed);
        Assert.Equal(lesson, lab.Remote.Lesson);

        var closed = ClassFolderText.HomeClosed(ClassSyncRig.MachineName);
        Assert.Equal((false, closed, string.Empty), await lab.Remote.CreateAsync("9º A"));
        Assert.Equal((false, closed), await lab.Remote.RenameAsync("3º B", "9º A"));
        Assert.Equal((false, closed), await lab.Remote.DeleteAsync("3º B"));
        Assert.Equal(closed, (await Assert.ThrowsAsync<ClassFolderRefusedException>(() => lab.Remote.StartAsync("3º B"))).Message);
        Assert.Equal(
            closed, (await Assert.ThrowsAsync<ClassFolderRefusedException>(() => lab.Remote.EndAsync(Turma, EndChoice.DeleteAll))).Message);
        Assert.Equal(
            ClassFolderText.LessonAlreadyOver("3º B"),
            (await Assert.ThrowsAsync<ClassFolderRefusedException>(() => lab.Remote.EndAsync("3º B", EndChoice.Keep))).Message);
        Assert.All(rig.Pcs, pc => Assert.Equal(lesson, pc.Agent.Current));

        var ended = await lab.Remote.EndAsync(Turma, EndChoice.Keep);

        Assert.Equal(AllPcs, ended.Select(line => line.Name));
        Assert.All(ended, line => Assert.True(line.Ok, $"{line.Name}: {line.Detail}"));
        // The same lesson, ended, newer than the Agents' (the clock stands still in the rig).
        var expected = lesson with { SavedAt = lesson.SavedAt + 1, Ended = true };
        Assert.All(rig.Pcs, pc => Assert.Equal(expected, pc.Agent.Current));
        Assert.Equal(expected, lab.Remote.Lesson);
        Assert.Empty(lab.Remote.LastEndProblems); // nothing was done to the home's files
        Assert.Equal(new[] { Turma, "3º B" }, rig.Store.List().Select(summary => summary.Turma));
        foreach (var pc in rig.Pcs)
        {
            pc.Follow();
        }

        var reopened = rig.NewSync();

        await Until(() => Task.FromResult(reopened.Lesson == expected));
        Assert.False(rig.Store.IsCollected(lesson.LessonId));
        await reopened.StartAsync("3º B");
        foreach (var pc in rig.Pcs)
        {
            pc.Follow();
        }
        await rig.Rounds(reopened, 1);
        Assert.Equal(new[] { "depois.txt" }, rig.StoreFiles(Turma));
    }

    // Review Focus 4, the home's firewall rule missing: this PC never reached
    // the home, yet it shows the lesson the Agents follow, and can end it.
    [Fact]
    public async Task AHomeNeverReached_ShowsTheAgentsLesson_AndTheRemoteCanEndIt()
    {
        using var rig = new ClassSyncRig(pcs: 2, homes: 2);
        using var lab = new TwoMasters(rig);
        await lab.Home.StartAsync(Turma);
        var lesson = lab.Home.Lesson;
        lab.CloseHome();

        for (var poll = 0; poll < RemoteClassFolderController.ClosedAfter; poll++)
        {
            await lab.Remote.PollAsync();
        }

        Assert.True(lab.Remote.HomeClosed);
        Assert.Equal(lesson, lab.Remote.Lesson);
        var ended = await lab.Remote.EndAsync(Turma, EndChoice.Keep);
        Assert.Equal(new[] { "PC1", "PC2" }, ended.Select(line => line.Name));
        Assert.All(rig.Pcs, pc => Assert.False(pc.Agent.Current.IsOn));
        Assert.All(rig.Pcs, pc => Assert.Equal(lesson.LessonId, pc.Agent.Current.LessonId));
        Assert.False(lab.Remote.Lesson.IsOn);
    }

    // No PC answering the remote's question: nothing is ended, and the teacher is told why.
    [Fact]
    public async Task WithTheHomeClosed_AndNoPcAnswering_TheEndSaysSo()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new TwoMasters(rig);
        await lab.Remote.StartAsync(Turma);
        lab.CloseHome();
        for (var poll = 0; poll < RemoteClassFolderController.ClosedAfter; poll++)
        {
            await lab.Remote.PollAsync();
        }
        rig.Offline["PC1"] = true;

        var refused = await Assert.ThrowsAsync<ClassFolderRefusedException>(() => lab.Remote.EndAsync(Turma, EndChoice.Keep));

        Assert.Equal(ClassFolderText.EndFailed(Turma, ClassFolderText.NoPcAnswered), refused.Message);
        Assert.True(rig.Pcs[0].Agent.Current.IsOn);
    }

    // The loop polls by itself while it runs, and follows the home.
    [Fact]
    public async Task Started_ItFollowsTheHome_AndFindsItClosed()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new TwoMasters(rig, pollInterval: TimeSpan.FromMilliseconds(50));
        var changes = 0;
        lab.Remote.Changed += () => Interlocked.Increment(ref changes);
        lab.Remote.Start();
        try
        {
            await lab.Home.StartAsync(Turma);
            await Until(() => Task.FromResult(lab.Remote.Lesson == lab.Home.Lesson));
            Assert.True(changes > 0);

            lab.CloseHome();
            await Until(() => Task.FromResult(lab.Remote.HomeClosed));
            Assert.Equal(lab.Home.Lesson, lab.Remote.Lesson);
        }
        finally
        {
            lab.Remote.Stop();
        }
    }

    // The two teacher PCs: the home (the rig's Master, PROF-1: its sync, its
    // window's controller and its host) and a colleague's (the rig's
    // OtherMaster, with the same classroom list and clock), which finds the
    // home where the room's Agents say it is.
    private sealed class TwoMasters : IDisposable
    {
        private readonly ClassSyncRig _rig;
        private int _homePort;

        public TwoMasters(ClassSyncRig rig, TimeSpan? pollInterval = null)
        {
            _rig = rig;
            Sync = rig.NewSync();
            Home = LocalClassFolderControllerTests.NewController(rig, Sync);
            Host = OpenHost();
            Remote = new RemoteClassFolderController(
                rig.OtherMaster,
                () => new ClassHome(rig.Master.Thumbprint, ClassSyncRig.MachineName, IPAddress.Loopback.ToString(), _homePort, 1),
                () => rig.Machines, machine => !rig.Offline.ContainsKey(machine.Name), pollInterval ?? TimeSpan.FromHours(1),
                rig.AgentPort, () => rig.Now);
        }

        public ClassFolderSync Sync { get; }

        public LocalClassFolderController Home { get; }

        public ClassFolderHost Host { get; private set; }

        public RemoteClassFolderController Remote { get; }

        // The home's Master closed: neither its host nor its sync runs.
        public void CloseHome()
        {
            Host.Dispose();
            Sync.Dispose();
        }

        // The home's Master listening again (on another port here; the Agents would say where).
        public void ReopenHost()
        {
            Host.Dispose();
            Host = OpenHost();
        }

        public void Dispose()
        {
            Remote.Dispose();
            Host.Dispose();
        }

        private ClassFolderHost OpenHost()
        {
            var host = Open(_rig.Master, Home, Trusting(_rig.OtherMaster));
            _homePort = host.Port;
            return host;
        }
    }

    private static ClassFolderHost Open(
        X509Certificate2 certificate, IClassFolderController controller, Func<string, IPAddress, Task<bool>> isTrusted) =>
        ClassFolderHost.Open(certificate, controller, isTrusted, 0, _ => { }, IPAddress.Loopback)
        ?? throw new InvalidOperationException("The host could not listen.");

    private static Func<string, IPAddress, Task<bool>> Trusting(X509Certificate2 colleague) =>
        (thumbprint, _) => Task.FromResult(string.Equals(thumbprint, colleague.Thumbprint, StringComparison.OrdinalIgnoreCase));

    private static async Task Until(Func<Task<bool>> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!await condition())
        {
            Assert.True(clock.Elapsed < Wait, "Timed out waiting for the condition");
            await Task.Delay(50);
        }
    }
}
