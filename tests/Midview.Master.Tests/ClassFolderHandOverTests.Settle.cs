using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderHandOver after its copy went to a colleague's PC (Task 7 fix
// round 1, Ruling 24): until that PC says how its take-over went
// (RemoteHandOverDone, or RemoteTakeBack when it can't tell), or SettleWait
// passes, this PC is busy (IsBusy): its role neither stands down nor closes
// the host that answer must reach. And a hand-over the room has left behind
// (the Agents name this PC above the generation it handed over at) no longer
// counts (minor 2).
public sealed partial class ClassFolderHandOverTests
{
    [Fact]
    public void TheSettleWait_CoversTheTakingPcsClaimsAndALostAnswer()
    {
        Assert.Equal(2 * ClassFolderHandOver.DefaultNetworkWait + ClassFolderHandOver.DefaultLostAnswerWait, ClassFolderHandOver.DefaultSettleWait);
        Assert.InRange(ClassFolderHandOver.DefaultSettleWait, TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(5));
    }

    // Between the copy's end and RemoteHandOverDone (the taking PC claiming the
    // Agents meanwhile), the old home is busy; once told, it isn't.
    [Fact]
    public async Task ACopySent_KeepsTheOldHomeBusy_UntilTheTakingPcSaysHowItWent()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var busyWhileClaiming = new List<bool>();
        lab.Taker.SendClaim = (machine, claim, sending) =>
        {
            lock (busyWhileClaiming)
            {
                busyWhileClaiming.Add(lab.Giver.IsBusy);
            }
            return ClassFolderClient.ClaimHome(machine, rig.OtherMaster, claim, rig.AgentPort, sending);
        };
        Assert.False(lab.Giver.IsBusy);

        var result = await lab.Taker.TakeOverAsync(lab.Home);

        Assert.True(result.Ok, result.Detail);
        Assert.Equal(new[] { true, true, true }, busyWhileClaiming);
        Assert.False(lab.Giver.IsBusy);
    }

    // A take-over that never says how it went (its claims refused: it puts
    // everything back and tells nobody) stops keeping the old home busy after
    // SettleWait, and the old home is told to look at the room again.
    [Fact]
    public async Task ACopyThatNeverSettles_StopsKeepingTheOldHomeBusy_AfterTheWait()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.Giver.SettleWait = TimeSpan.FromSeconds(1);
        lab.Taker.SendClaim = (_, _, _) => (false, "recusado", false);
        var changes = 0;
        lab.Giver.Changed += () => Interlocked.Increment(ref changes);

        var result = await lab.Taker.TakeOverAsync(lab.Home);

        Assert.False(result.Ok);
        Assert.True(lab.Giver.IsBusy);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (lab.Giver.IsBusy)
        {
            Assert.True(DateTime.UtcNow < deadline, "still busy");
            await Task.Delay(50);
        }
        Assert.Equal(1, Volatile.Read(ref changes));
    }

    // A RemoteHandOverDone this PC refuses settles the copy all the same, and says so.
    [Fact]
    public async Task ARefusedDone_StillSettlesTheCopy()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.Taker.SendClaim = (_, _, _) => (false, "recusado", false);
        await lab.Taker.TakeOverAsync(lab.Home);
        Assert.True(lab.Giver.IsBusy);
        var changes = 0;
        lab.Giver.Changed += () => Interlocked.Increment(ref changes);

        Assert.False(lab.SendHandedOver(NewHome).Ok); // no Agent keeps PROF-2

        Assert.False(lab.Giver.IsBusy);
        Assert.Equal(1, Volatile.Read(ref changes));
    }

    // Minor 2: handed over to PROF-2 at G+1, then the room decided for this PC
    // (an undecided take-over of its own, say: the Agents name it at G+5). That
    // hand-over is behind it: a colleague's take-back is taken again.
    [Fact]
    public async Task AHandOverTheRoomLeftBehind_NoLongerRefusesATakeBack()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = ClassHomeFileMaster.Read(rig.HomePath)!.Value;
        Assert.True(ClassFolderClient.ClaimHome(
            rig.Pcs[0].Machine, rig.OtherMaster, new ClassHomeClaim(NewHome, ClassFolderRemote.Port, true, generation + 1), rig.AgentPort).Ok);
        Assert.True(lab.SendHandedOver(NewHome).Ok);
        Assert.True(lab.Giver.HandedOver);
        foreach (var pc in rig.Pcs)
        {
            Assert.True(ClassFolderClient.ClaimHome(
                pc.Machine, rig.Master, new ClassHomeClaim(OldHome, ClassFolderRemote.Port, true, generation + 5), rig.AgentPort).Ok);
        }

        var answer = await lab.Giver.TakeBackAsync(generation + 6, rig.OtherMaster.Thumbprint);

        Assert.True(answer.Ok, answer.Detail);
        Assert.False(lab.Giver.HandedOver);
        Assert.Null(ClassHomeFileMaster.ReadHandedOverTo(rig.HomePath));
        Assert.All(rig.Pcs, pc => Assert.Equal(OldHome, pc.Agent.CurrentHome?.Name));
    }

    // Minor 2: the room naming this PC above the generation it handed over at
    // voids that hand-over (in memory and on file); at or below it, not.
    [Fact]
    public async Task SeenAsTheHomeAboveTheHandOver_VoidsIt()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = ClassHomeFileMaster.Read(rig.HomePath)!.Value;
        Assert.True(ClassFolderClient.ClaimHome(
            rig.Pcs[0].Machine, rig.OtherMaster, new ClassHomeClaim(NewHome, ClassFolderRemote.Port, true, generation + 1), rig.AgentPort).Ok);
        Assert.True(lab.SendHandedOver(NewHome).Ok);

        lab.Giver.SeenAsHome(generation);
        lab.Giver.SeenAsHome(generation + 1);
        Assert.True(lab.Giver.HandedOver);
        Assert.NotNull(ClassHomeFileMaster.ReadHandedOverTo(rig.HomePath));

        lab.Giver.SeenAsHome(generation + 2);

        Assert.False(lab.Giver.HandedOver);
        Assert.Null(ClassHomeFileMaster.ReadHandedOverTo(rig.HomePath));
    }
}
