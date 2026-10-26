using Midview.Master;
using Midview.Protocol;
using Xunit;

// What MainWindow and ClassFolderRole go by: IsBusy while a take-over
// runs on this PC (closing the Master waits for it to end), and Changed once
// it is over, on the PC taking the folders over and on the old home (each
// role then asks the room again, and the two PCs swap modes).
public sealed partial class ClassFolderHandOverTests
{
    [Fact]
    public async Task ATakeOver_IsBusyWhileItRuns_AndBothPcsSayWhenItIsOver()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        var taker = 0;
        var giver = 0;
        lab.Taker.Changed += () => Interlocked.Increment(ref taker);
        lab.Giver.Changed += () => Interlocked.Increment(ref giver);
        var busy = new List<bool>();
        var progress = new Inline(_ =>
        {
            lock (busy)
            {
                busy.Add(lab.Taker.IsBusy);
            }
        });
        Assert.False(lab.Taker.IsBusy);

        var result = await lab.Taker.TakeOverAsync(lab.Home, progress);

        Assert.True(result.Ok, result.Detail);
        Assert.NotEmpty(busy);
        Assert.All(busy, Assert.True);
        Assert.False(lab.Taker.IsBusy);
        Assert.False(lab.Giver.IsBusy);
        Assert.Equal(1, Volatile.Read(ref taker));
        Assert.Equal(1, Volatile.Read(ref giver)); // it stood down before it answered
    }

    // An old home that handed the folders over still refuses a
    // take-back once its Master was opened again (a new ClassFolderHandOver over
    // the same files), even from the PC it handed them to with a straggler
    // still naming it; taking the folders over again lifts that.
    [Fact]
    public async Task AnOldHomeOpenedAgainAfterHandingTheFoldersOver_StillDoesntTakeTheRoomBack()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = ClassHomeFileMaster.Read(rig.HomePath)!.Value;
        Assert.True(ClassFolderClient.ClaimHome(
            rig.Pcs[0].Machine, rig.OtherMaster, new ClassHomeClaim(NewHome, ClassFolderRemote.Port, true, generation + 1), rig.AgentPort).Ok);
        Assert.True(lab.SendHandedOver(NewHome).Ok);
        Assert.Equal((NewHome, generation + 1), (ClassHomeFileMaster.ReadHandedOverTo(rig.HomePath)?.Name, ClassHomeFileMaster.ReadHandedOverTo(rig.HomePath)?.Generation));
        var homes = lab.Homes();
        var reopened = new ClassFolderHandOver(
            rig.Store, lab.OldSync, rig.Master, OldHome, () => rig.Machines, machine => !rig.Offline.ContainsKey(machine.Name),
            () => rig.Now, rig.AgentPort, homePath: rig.HomePath);

        var answer = await reopened.TakeBackAsync(generation + 1, rig.OtherMaster.Thumbprint);

        Assert.Equal((false, ClassFolderText.NotTheHomeAnyMore(OldHome)), answer);
        Assert.Equal(homes, lab.Homes());
        Assert.Equal(generation, ClassHomeFileMaster.Read(rig.HomePath));

        var tookOver = await reopened.TakeOverUnreachableAsync(NewHome);

        Assert.True(tookOver.Ok, tookOver.Detail);
        Assert.Null(ClassHomeFileMaster.ReadHandedOverTo(rig.HomePath));
        Assert.False(reopened.HandedOver);
    }

    // A take-over that didn't happen says so too: which PC keeps the folders is asked again all the same.
    [Fact]
    public async Task ATakeOverRefused_StillSaysItIsOver()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        await lab.OldSync.StartAsync(Turma);
        var taker = 0;
        lab.Taker.Changed += () => Interlocked.Increment(ref taker);

        var result = await lab.Taker.TakeOverAsync(lab.Home);

        Assert.False(result.Ok);
        Assert.Equal(1, Volatile.Read(ref taker));
        Assert.False(lab.Taker.IsBusy);
    }
}
