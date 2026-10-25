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
