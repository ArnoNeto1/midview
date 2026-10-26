using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// ThumbnailPoller taking back a straggler (Ruling 9, Task 7): while this PC is
// the class folders' home, an Agent that keeps an older home (another PC's,
// or this PC's at an older generation: it was off when this PC took the
// folders over) is claimed with this PC's generation as its machine comes
// online. Against the rig's real Agent and Session.
public partial class ThumbnailPollerTests
{
    private const long HomeGeneration = 5_000;

    private static ClassHomeClaim HomeClaim() =>
        new(ClassSyncRig.MachineName, ClassFolderRemote.Port, TakeOver: true, HomeGeneration);

    [Theory]
    [InlineData("PROF-2", 4_000, true)] // another PC, older: taken back
    [InlineData(ClassSyncRig.MachineName, 4_000, true)] // this PC at an older generation: brought up to it
    [InlineData("PROF-2", 6_000, false)] // another PC, newer: left as it is (this PC isn't the home any more)
    [InlineData("PROF-2", HomeGeneration, false)] // the same generation: left as it is
    public async Task AnAgentKeepingAnOlderHome_IsClaimed_AsItsMachineAnswers(string keeps, long at, bool claimed)
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        var pc = rig.Pcs[0];
        ClaimFor(rig, pc, keeps == ClassSyncRig.MachineName ? rig.Master : rig.OtherMaster, keeps, at);
        using var poller = new ThumbnailPoller(pc.Machine, rig.Master, rig.SessionPort, rig.AgentPort, classHomeClaim: HomeClaim);

        if (claimed)
        {
            poller.Start();
            await WaitUntil(() => pc.Agent.CurrentHome?.Generation == HomeGeneration);
            Assert.Equal(ClassSyncRig.MachineName, pc.Agent.CurrentHome!.Name);
            Assert.Equal(rig.Master.Thumbprint, pc.Agent.CurrentHome.Thumbprint, ignoreCase: true);
        }
        else
        {
            await ThreeThumbnails(poller);
            Assert.Equal((keeps, at), (pc.Agent.CurrentHome!.Name, pc.Agent.CurrentHome.Generation));
        }
    }

    // While this PC isn't the home (no claim), nothing is asked.
    [Fact]
    public async Task WithNoClaim_NothingIsTaken()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        var pc = rig.Pcs[0];
        ClaimFor(rig, pc, rig.OtherMaster, "PROF-2", 4_000);
        using var poller = new ThumbnailPoller(pc.Machine, rig.Master, rig.SessionPort, rig.AgentPort, classHomeClaim: () => null);

        await ThreeThumbnails(poller);

        Assert.Equal("PROF-2", pc.Agent.CurrentHome?.Name);
    }

    // The Agent refuses a take-over while it follows a lesson of its home
    // (Ruling 7): that PC is left in it, and taken back once the lesson is over.
    [Fact]
    public async Task AnAgentInALessonOfItsHome_IsTakenBackOnlyOnceItEnds()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        var pc = rig.Pcs[0];
        ClaimFor(rig, pc, rig.OtherMaster, "PROF-2", 4_000);
        var now = new DateTimeOffset(rig.Now).ToUnixTimeSeconds();
        var lesson = new ClassLesson(now, Guid.NewGuid(), ClassSyncRig.Turma, now, Ended: false);
        SetLessonOn(rig, pc, lesson);
        using var poller = new ThumbnailPoller(
            pc.Machine, rig.Master, rig.SessionPort, rig.AgentPort, agentRetryInterval: TimeSpan.FromMilliseconds(200),
            classHomeClaim: HomeClaim);

        await ThreeThumbnails(poller);
        Assert.Equal("PROF-2", pc.Agent.CurrentHome?.Name);
        Assert.Equal(lesson, pc.Agent.Current);

        SetLessonOn(rig, pc, lesson with { SavedAt = now + 1, Ended = true });

        await WaitUntil(() => pc.Agent.CurrentHome?.Name == ClassSyncRig.MachineName);
        Assert.Equal(HomeGeneration, pc.Agent.CurrentHome!.Generation);
    }

    private static void ClaimFor(ClassSyncRig rig, StudentPc pc, X509Certificate2 master, string name, long generation)
    {
        var (ok, detail, _) = ClassFolderClient.ClaimHome(
            pc.Machine, master, new ClassHomeClaim(name, ClassFolderRemote.Port, TakeOver: false, generation), rig.AgentPort);
        Assert.True(ok, detail);
    }

    private static void SetLessonOn(ClassSyncRig rig, StudentPc pc, ClassLesson lesson)
    {
        var (ok, detail) = ClassFolderClient.SetLesson(pc.Machine, rig.OtherMaster, lesson, rig.AgentPort);
        Assert.True(ok, detail);
    }
}
