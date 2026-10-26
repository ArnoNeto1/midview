using System.Collections.Concurrent;
using System.IO;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderRole around a take-over, with the trackers reading the room by
// themselves every 200 ms, as in a lab only faster (Task 7 fix round 1,
// Ruling 24): the old home keeps its host open, and its mode, until the PC
// taking the folders over says how it went; that PC doesn't go Local before
// its own take-over is over. Also: a hand-over the room left behind no
// longer makes a single reading sure (minor 2), and a host that can't listen
// is tried again soon (minor 5).
public sealed partial class ClassFolderRoleTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(200);

    // Two teacher PCs reading the room by themselves, PROF-1 the home.
    private static async Task<(TeacherPc Home, TeacherPc Other)> TwoFastPcsAsync(ClassSyncRig rig)
    {
        var home = new TeacherPc(rig, Prof1, rig.Master, confirmWait: TimeSpan.FromMilliseconds(100), trackerInterval: Fast);
        var other = new TeacherPc(rig, Prof2, rig.OtherMaster, confirmWait: TimeSpan.FromMilliseconds(100), trackerInterval: Fast);
        try
        {
            await home.Sync.StartAsync(Turma);
            await home.Sync.EndAsync(EndChoice.Keep);
            await Until(() => home.Role.Mode == ClassFolderMode.Local && other.Role.Mode == ClassFolderMode.Remote);
            return (home, other);
        }
        catch (Exception)
        {
            home.Dispose();
            other.Dispose();
            throw;
        }
    }

    // The OK path, with one Agent answering PROF-2's claim late (held behind
    // its lesson lock, say): the other two named PROF-2 seconds before
    // RemoteHandOverDone comes, and PROF-1 is still there to take it.
    [Fact]
    public async Task ATakeOverWithAnAgentAnsweringLate_StillReachesTheOldHome()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var (prof1, prof2) = await TwoFastPcsAsync(rig);
        using var disposeHome = prof1;
        using var disposeOther = prof2;
        var late = rig.Pcs[2].Machine;
        prof2.HandOver.SendClaim = (machine, claim, sending) =>
        {
            var answer = ClassFolderClient.ClaimHome(machine, rig.OtherMaster, claim, rig.AgentPort, sending);
            if (machine == late)
            {
                Thread.Sleep(TimeSpan.FromSeconds(3));
            }
            return answer;
        };
        var remote = Assert.IsType<RemoteClassFolderController>(prof2.Role.Controller);

        var result = await remote.TakeOverAsync(prof2.HandOver, remote.Home, homeClosed: false);

        Assert.True(result.Ok, result.Detail);
        Assert.Contains(ClassFolderText.OldCopyAside(Prof1, "Turmas (passadas para PROF-2)"), result.Detail);
        Assert.True(Directory.Exists(Path.Combine(rig.Root, "Turmas (passadas para PROF-2)")));
        await Until(() => prof1.Role.Mode == ClassFolderMode.Remote && prof2.Role.Mode == ClassFolderMode.Local);
        Assert.False(Listens(prof1.HostPort));
        Assert.True(Listens(prof2.HostPort));
    }

    // Ruling 21(a): PC1 takes PROF-2's claim and its answer is lost, it gives no
    // answer to the re-read either, and the others refuse: PROF-2 can't tell,
    // and asks PROF-1 to take the room back. PROF-1, reading PC1 name PROF-2
    // meanwhile, still has its host open: it takes the room back. PROF-2 never
    // goes Local while its take-over runs, though PC1 names it.
    [Fact]
    public async Task AnUndecidedTakeOver_IsTakenBack_ThroughTheOldHomesHostStillOpen()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var (prof1, prof2) = await TwoFastPcsAsync(rig);
        using var disposeHome = prof1;
        using var disposeOther = prof2;
        var gone = rig.Pcs[0].Machine;
        (bool, string, bool) ClaimAndLoseTheAnswer(KnownMachine machine, ClassHomeClaim claim, Action sending)
        {
            ClassFolderClient.ClaimHome(machine, rig.OtherMaster, claim, rig.AgentPort, sending);
            throw new IOException("the answer was lost");
        }
        prof2.HandOver.SendClaim = (machine, claim, sending) =>
            machine == gone ? ClaimAndLoseTheAnswer(machine, claim, sending) : (false, "recusado", false);
        prof2.HandOver.ReadHome = machine => machine == gone
            ? throw new IOException("no answer")
            : ClassFolderClient.GetHome(machine, rig.OtherMaster, rig.AgentPort);
        var modesOfProf2 = new ConcurrentQueue<ClassFolderMode?>();
        prof2.Role.Changed += () => modesOfProf2.Enqueue(prof2.Role.Mode);
        var remote = Assert.IsType<RemoteClassFolderController>(prof2.Role.Controller);

        var result = await remote.TakeOverAsync(prof2.HandOver, remote.Home, homeClosed: false);

        Assert.Equal((false, ClassFolderText.TakeOverTakenBack(Prof1, new[] { "PC1" })), (result.Ok, result.Detail));
        Assert.All(rig.Pcs, pc => Assert.Equal(Prof1, pc.Agent.CurrentHome?.Name));
        Assert.DoesNotContain(ClassFolderMode.Local, modesOfProf2);
        Assert.False(prof1.HandOver.IsBusy);
        Assert.Equal(ClassFolderMode.Local, prof1.Role.Mode);
        Assert.True(Listens(prof1.HostPort));
        Assert.Equal(ClassFolderMode.Remote, prof2.Role.Mode);
    }

    // Minor 2: once the room names this PC above the generation it handed the
    // folders over at, that hand-over is void: a single Remote reading is no
    // longer sure.
    [Fact]
    public async Task TheRoomNamingThisPcAboveItsHandOver_VoidsIt()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var prof1 = new TeacherPc(rig, Prof1, rig.Master);
        using var prof2 = new TeacherPc(rig, Prof2, rig.OtherMaster);
        await prof1.Sync.StartAsync(Turma);
        await prof1.Sync.EndAsync(EndChoice.Keep);
        await prof1.Role.RefreshAndActAsync();
        await prof2.Role.RefreshAndActAsync();
        await prof2.Role.RefreshAndActAsync();
        var remote = Assert.IsType<RemoteClassFolderController>(prof2.Role.Controller);
        Assert.True((await remote.TakeOverAsync(prof2.HandOver, remote.Home, homeClosed: false)).Ok);
        Assert.True(prof1.HandOver.HandedOver);
        await prof1.Role.RefreshAndActAsync();
        Assert.Equal(ClassFolderMode.Remote, prof1.Role.Mode);
        var prof2Home = rig.Pcs[0].Agent.CurrentHome!;
        ClaimEveryAgent(rig, rig.Master, Prof1, prof2Home.Generation + 10, takeOver: true);

        await prof1.Role.RefreshAndActAsync();

        Assert.False(prof1.HandOver.HandedOver);
        Assert.Equal(ClassFolderMode.Local, prof1.Role.Mode);
        await prof1.Role.ActOnAsync(new ClassHomeReading(prof2Home, ClassFolderMode.Remote, 3, StartedAt: Environment.TickCount64));
        Assert.Equal(ClassFolderMode.Local, prof1.Role.Mode);
    }

    // A reading the tracker began while a take-over wasn't over yet says what
    // the room said then: acted on after it is over, it counts for nothing (the
    // hand-over's Changed asks the room afresh). Here PROF-2's own reading,
    // begun mid take-over with PC1 naming it, would otherwise make it Local at
    // once though PROF-1 took the room back.
    [Fact]
    public async Task AReadingBegunBeforeATakeOverWasOver_CountsForNothing()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var prof1 = new TeacherPc(rig, Prof1, rig.Master);
        using var prof2 = new TeacherPc(rig, Prof2, rig.OtherMaster);
        await prof1.Sync.StartAsync(Turma);
        await prof1.Sync.EndAsync(EndChoice.Keep);
        await prof1.Role.RefreshAndActAsync();
        await prof2.Role.RefreshAndActAsync();
        await prof2.Role.RefreshAndActAsync();
        var begun = Environment.TickCount64;
        var remote = Assert.IsType<RemoteClassFolderController>(prof2.Role.Controller);
        prof2.HandOver.SendClaim = (_, _, _) => (false, "recusado", false); // not taken: nothing changes
        Assert.False((await remote.TakeOverAsync(prof2.HandOver, remote.Home, homeClosed: false)).Ok);
        var namingProf2 = new ClassHome(rig.OtherMaster.Thumbprint, Prof2, "127.0.0.1", prof2.HostPort, rig.NowTicks + 5);

        await prof2.Role.ActOnAsync(new ClassHomeReading(namingProf2, ClassFolderMode.Local, 3, StartedAt: begun));

        Assert.Equal(ClassFolderMode.Remote, prof2.Role.Mode);

        await prof2.Role.ActOnAsync(new ClassHomeReading(namingProf2, ClassFolderMode.Local, 3, StartedAt: Environment.TickCount64));

        Assert.Equal(ClassFolderMode.Local, prof2.Role.Mode); // a fresh one does
    }

    // Minor 5: a host that can't listen just now (the port not let go yet after
    // a quick switch back) is tried again in seconds, not at the next 30 s reading.
    [Fact]
    public async Task AHostThatCantListen_IsTriedAgainSoon()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var prof1 = new TeacherPc(rig, Prof1, rig.Master) { FailedOpens = 1 };

        await prof1.Role.RefreshAndActAsync();

        Assert.Equal(ClassFolderMode.Local, prof1.Role.Mode);
        Assert.Null(prof1.Role.Host);
        await Until(() => prof1.Role.Host is not null);
        Assert.True(Listens(prof1.HostPort));
    }
}
