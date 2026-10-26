using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderSync's start going by the newest generation the Agents keep for
// this PC (Ruling 21): a PC that was the home once keeps an older generation on
// file; when the Agents name it at a newer one (a take-over it made that its
// file doesn't have yet), that one is this PC's, saved, and the start takes
// the PCs that keep an older home back.
public sealed partial class ClassFolderSyncTests
{
    [Fact]
    public async Task AnOlderGenerationOnFile_GivesWayToTheNewerOneTheAgentsKeepForThisPc()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var generation = rig.NowTicks;
        ClassHomeFileMaster.Write(generation - 1, rig.HomePath);
        Assert.True(ClassFolderClient.ClaimHome(
            rig.Pcs[0].Machine, rig.Master, new ClassHomeClaim(ClassSyncRig.MachineName, ClassFolderRemote.Port, false, generation + 1),
            rig.AgentPort).Ok);
        foreach (var pc in rig.Pcs.Skip(1))
        {
            Assert.True(ClassFolderClient.ClaimHome(
                pc.Machine, rig.OtherMaster, new ClassHomeClaim(OtherPc, ClassFolderRemote.Port, false, generation), rig.AgentPort).Ok);
        }
        var sync = rig.NewSync();

        var started = await sync.StartAsync(Turma);

        Assert.All(started, line => Assert.True(line.Ok, $"{line.Machine.Name}: {line.Detail}"));
        Assert.True(sync.Lesson.IsOn);
        Assert.All(rig.Pcs, pc => Assert.Equal((ThisPc, generation + 1), (pc.Agent.CurrentHome!.Name, pc.Agent.CurrentHome.Generation)));
        Assert.Equal(generation + 1, ClassHomeFileMaster.Read(rig.HomePath));
    }

    // A generation on file past the latest one (kept from before that limit)
    // is claimed at the latest, which the Agents take, and kept so.
    [Fact]
    public async Task AGenerationOnFilePastTheLatest_IsClaimedAtTheLatest()
    {
        using var rig = new ClassSyncRig(homes: 3);
        ClassHomeFileMaster.Write(long.MaxValue, rig.HomePath);
        var sync = rig.NewSync();

        var started = await sync.StartAsync(Turma);

        Assert.All(started, line => Assert.True(line.Ok, $"{line.Machine.Name}: {line.Detail}"));
        Assert.All(rig.Pcs, pc => Assert.Equal((ThisPc, ClassHome.MaxGeneration), (pc.Agent.CurrentHome!.Name, pc.Agent.CurrentHome.Generation)));
        Assert.Equal(ClassHome.MaxGeneration, ClassHomeFileMaster.Read(rig.HomePath));
    }

    // A take-over's generation is newer than every home reported, and never
    // past the latest one, whatever a home reports (one kept from before that limit).
    [Fact]
    public void ATakeOversGeneration_NeverPassesTheLatest()
    {
        ClassHome At(long generation) => new(new string('A', 40), OtherPc, "10.0.0.6", ClassFolderRemote.Port, generation);

        Assert.Equal(ClassHome.MaxGeneration, ClassHomeFileMaster.TakeOverGeneration(500, new[] { At(long.MaxValue) }));
        Assert.Equal(ClassHome.MaxGeneration, ClassHomeFileMaster.TakeOverGeneration(500, new[] { At(ClassHome.MaxGeneration) }));
        Assert.Equal(ClassHome.MaxGeneration, ClassHomeFileMaster.TakeOverGeneration(500, new[] { At(ClassHome.MaxGeneration - 1) }));
        Assert.Equal(1_001, ClassHomeFileMaster.TakeOverGeneration(500, new[] { At(1_000) }));
    }

    // Two PCs claiming the room at the latest generation: the one an Agent
    // keeps there can't be taken back (nothing is newer), so it keeps the
    // room even against a name that sorts after its own. Nothing starts here,
    // and no PC is in a lesson (none split between two homes).
    [Fact]
    public async Task ARivalAtTheLatestGeneration_KeepsTheRoom_EvenAgainstANameThatSortsAfter()
    {
        using var rig = new ClassSyncRig(homes: 3);
        ClassHomeFileMaster.Write(ClassHome.MaxGeneration, rig.HomePath);
        ClaimFor(rig, rig.Pcs[0], "PROF-0", takeOver: false, ClassHome.MaxGeneration);
        var sync = rig.NewSync();

        var refused = await sync.StartAsync(Turma);

        Assert.Equal(3, refused.Count);
        Assert.All(refused, line => Assert.Equal((false, ClassHomeClaim.OtherHomeText + "PROF-0"), (line.Ok, line.Detail)));
        Assert.False(sync.Lesson.IsOn);
        Assert.All(rig.Pcs, pc => Assert.False(pc.Agent.Current.IsOn));
    }

    // An Agent that names another PC never counts toward this PC's generation.
    [Fact]
    public async Task AnotherPcsNewerGeneration_IsNeverTakenForThisPcs()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var generation = rig.NowTicks;
        ClassHomeFileMaster.Write(generation - 1, rig.HomePath);
        Assert.True(ClassFolderClient.ClaimHome(
            rig.Pcs[0].Machine, rig.OtherMaster, new ClassHomeClaim(OtherPc, ClassFolderRemote.Port, false, generation + 1),
            rig.AgentPort).Ok);
        var sync = rig.NewSync();

        var started = await sync.StartAsync(Turma);

        Assert.False(sync.Lesson.IsOn);
        Assert.Contains(started, line => !line.Ok && line.Detail == ClassHomeClaim.OtherHomeText + OtherPc);
        Assert.Equal(generation - 1, ClassHomeFileMaster.Read(rig.HomePath));
    }
}
