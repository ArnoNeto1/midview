using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderSync with one teacher PC, now that an Agent takes an end only of
// the lesson it follows: a PC that missed a lesson's end and the next start
// still gets out of its old lesson as before.
public sealed partial class ClassFolderSyncTests
{
    // PC1 was off for the end of 3º A and the whole lesson of 3º B; its Agent
    // is back for 3º B's end, still in 3º A, before the grid sees the PC (no
    // round reaches it). The end reaches it all the same: this PC ends 3º A
    // there first (a lesson it ran, over), and PC1's line says the lesson ended.
    [Fact]
    public async Task APcThatMissedALessonsEndAndTheNextStart_IsEndedAtTheNextEnd()
    {
        using var rig = new ClassSyncRig();
        var pc1 = rig.Pcs[0];
        var visible = rig.Machines.ToList();
        var sync = new ClassFolderSync(
            rig.Store, rig.Master, () => visible, machine => !rig.Offline.ContainsKey(machine.Name), () => rig.Now,
            ClassSyncRig.MachineName, agentPort: rig.AgentPort, sessionPort: rig.SessionPort, interval: TimeSpan.FromHours(1),
            lessonPath: rig.LessonPath, homePath: rig.HomePath);
        try
        {
            await sync.StartAsync(Turma);
            var missed = sync.Lesson;
            visible = rig.Machines.Where(machine => machine != pc1.Machine).ToList(); // PC1 off
            await sync.EndAsync(EndChoice.Keep);
            await sync.StartAsync("3º B");
            visible = rig.Machines.ToList(); // its Agent back on
            rig.Offline[pc1.Name] = true; // not seen by the grid yet
            Assert.Equal(missed, pc1.Agent.Current);

            var ended = await sync.EndAsync(EndChoice.Keep);

            Assert.All(ended, line => Assert.True(line.Ok, $"{line.Machine.Name}: {line.Detail}"));
            Assert.Equal("aula encerrada", ended.Single(line => line.Machine == pc1.Machine).Detail);
            Assert.Equal(sync.Lesson, pc1.Agent.Current);
        }
        finally
        {
            sync.Dispose();
        }
    }
}
