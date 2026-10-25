using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderSync's start with an Agent busy with its student's folders
// (Rulings 18 and 19): its claim is refused as busy (ClassHomeClaim.BusyText),
// which is an Agent that didn't answer, never another home. A busy claim is
// asked once more; still busy, that PC's line says so rather than naming the
// home it still keeps. The rig's Agents keep the home; BusyAgents holds their
// lesson locks, and frees them when the sync's log says what the test waits for.
public sealed partial class ClassFolderSyncTests
{
    // The start goes on, and the busy PC (already this PC's) gets the lesson once it is free.
    [Fact]
    public async Task AStartWithABusyAgent_GoesOn_AsWithOneThatDidntAnswer()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var log = new FreeingLog();
        using var sync = SyncWithLog(rig, log);
        await sync.StartAsync(Turma);
        await sync.EndAsync(EndChoice.Keep);
        var generation = ClassHomeFileMaster.Read(rig.HomePath);
        var homes = rig.Pcs.Select(pc => pc.Agent.CurrentHome).ToList();
        IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)> started;
        using (var busy = new BusyAgents(rig.Pcs.Take(1)))
        {
            // Free once its claim was refused both times (its lesson then waits for the lock).
            log.FreeWhen(busy, $"PC1 didn't take this PC as the class folders' home: {ClassHomeClaim.BusyText}");
            started = await sync.StartAsync("3º B");
        }

        Assert.All(started, line => Assert.True(line.Ok, $"{line.Machine.Name}: {line.Detail}"));
        Assert.True(sync.Lesson.IsOn);
        Assert.Equal("3º B", sync.Lesson.Turma);
        Assert.All(rig.Pcs, pc => Assert.Equal(sync.Lesson, pc.Agent.Current));
        Assert.Equal(generation, ClassHomeFileMaster.Read(rig.HomePath));
        Assert.Equal(homes, rig.Pcs.Select(pc => pc.Agent.CurrentHome));
        Assert.True(log.Has("PC1 was busy"));
    }

    // PC1 was off when this PC took the folders over, so it still keeps PROF-2
    // (older): the start takes it back. Busy at the first try, it is asked once
    // more: free by then, it takes this PC and gets the lesson; still busy, its
    // line says it is busy, not "As pastas das turmas ficam no PC PROF-2".
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AStragglerBusyAtTheStart_IsAskedAgain_AndItsLineSaysBusy(bool stillBusy)
    {
        using var rig = new ClassSyncRig(homes: 3);
        var log = new FreeingLog();
        using var sync = SyncWithLog(rig, log);
        Assert.True(ClassFolderClient.ClaimHome(
            rig.Pcs[0].Machine, rig.OtherMaster, new ClassHomeClaim("PROF-2", ClassFolderRemote.Port, false, rig.NowTicks - 1000),
            rig.AgentPort).Ok);
        rig.Offline["PC1"] = true;
        await sync.StartAsync(Turma);
        await sync.EndAsync(EndChoice.Keep);
        rig.Offline.Clear();
        Assert.Equal("PROF-2", rig.Pcs[0].Agent.CurrentHome!.Name);
        IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)> started;
        using (var busy = new BusyAgents(rig.Pcs.Take(1)))
        {
            log.FreeWhen(busy, stillBusy
                ? $"PC1 didn't take this PC as the class folders' home: {ClassHomeClaim.BusyText}"
                : "PC1 was busy");
            started = await sync.StartAsync("3º B");
        }

        var pc1 = started.Single(line => line.Machine.Name == "PC1");
        Assert.All(started.Where(line => line.Machine.Name != "PC1"), line => Assert.True(line.Ok, $"{line.Machine.Name}: {line.Detail}"));
        if (stillBusy)
        {
            Assert.Equal((false, ClassHomeClaim.BusyText), (pc1.Ok, pc1.Detail));
            Assert.Equal("PROF-2", rig.Pcs[0].Agent.CurrentHome!.Name);
        }
        else
        {
            Assert.True(pc1.Ok, pc1.Detail);
            Assert.Equal(ClassSyncRig.MachineName, rig.Pcs[0].Agent.CurrentHome!.Name);
            Assert.Equal(sync.Lesson, rig.Pcs[0].Agent.Current);
        }
    }

    // Ruling 22 (Task 7a, item 8): "no lesson on" and the stand-down are one
    // step under the lesson lock. A start under way (held up by a busy PC)
    // finishes first; the stand-down then finds its lesson on, says so, and
    // leaves it as it is.
    [Fact]
    public async Task StandingDownIfNoLesson_WhileAStartIsUnderWay_KeepsThatLesson()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var log = new FreeingLog();
        using var sync = SyncWithLog(rig, log);
        await sync.StartAsync(Turma);
        await sync.EndAsync(EndChoice.Keep);
        Task<ClassLesson?> standingDown;
        using (var busy = new BusyAgents(rig.Pcs.Take(1)))
        {
            using var claiming = new ManualResetEventSlim();
            log.When("PC1 was busy", claiming.Set);
            var starting = sync.StartAsync("3º B");
            Assert.True(claiming.Wait(TimeSpan.FromSeconds(10))); // the start holds the lesson lock
            standingDown = sync.StandDownIfNoLessonAsync();
            busy.Free();
            await starting;
        }

        var kept = await standingDown;
        Assert.True(sync.Lesson.IsOn);
        Assert.Equal("3º B", sync.Lesson.Turma);
        Assert.Equal(sync.Lesson, kept);
    }

    // With no lesson on, it stands down: no lesson ever saved, as StandDownAsync.
    [Fact]
    public async Task StandingDownIfNoLesson_WithNoneOn_StandsDown()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        await sync.EndAsync(EndChoice.Keep);

        Assert.Null(await sync.StandDownIfNoLessonAsync());
        Assert.Equal(ClassLesson.None(0), sync.Lesson);
        Assert.Equal(ClassLesson.None(0), ClassLessonFileMaster.Read(rig.LessonPath));
    }

    // Ruling 22 (Task 7a, item 9): an Agent busy with its student's folders
    // when the lesson's end reaches it refuses it at once, never taking it
    // later (this PC's poller hands it over then); its line says so, without
    // asking the teacher to try again.
    [Fact]
    public async Task AnEnd_ToABusyAgent_IsRefusedAtOnce_AndItsLineSaysMidviewTriesAgain()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)> ended;
        using (new BusyAgents(rig.Pcs.Take(1)))
        {
            ended = await sync.EndAsync(EndChoice.Keep);
        }

        var pc1 = ended.Single(line => line.Machine.Name == "PC1");
        Assert.Equal((false, "ocupado agora; o Midview tenta de novo em instantes"), (pc1.Ok, pc1.Detail));
        Assert.All(ended.Where(line => line.Machine.Name != "PC1"), line => Assert.True(line.Ok, line.Detail));
        Assert.True(rig.Pcs[0].Agent.Current.IsOn);
        Assert.False(sync.Lesson.IsOn);
    }

    private static ClassFolderSync SyncWithLog(ClassSyncRig rig, FreeingLog log) => new(
        rig.Store, rig.Master, () => rig.Machines, machine => !rig.Offline.ContainsKey(machine.Name), () => rig.Now,
        ClassSyncRig.MachineName, agentPort: rig.AgentPort, sessionPort: rig.SessionPort, interval: TimeSpan.FromHours(1),
        lessonPath: rig.LessonPath, homePath: rig.HomePath, log: log.Add);

    // The sync's log, which frees busy Agents (or does what else the test
    // says) when a line says what the test waits for, once.
    private sealed class FreeingLog
    {
        private readonly List<string> _lines = new();
        private (string Text, Action Act)? _when;

        public void FreeWhen(BusyAgents busy, string text) => When(text, busy.Free);

        public void When(string text, Action act)
        {
            lock (_lines)
            {
                _when = (text, act);
            }
        }

        public void Add(string line)
        {
            Action? act = null;
            lock (_lines)
            {
                _lines.Add(line);
                if (_when is { } when && line.Contains(when.Text, StringComparison.Ordinal))
                {
                    act = when.Act;
                    _when = null;
                }
            }
            act?.Invoke();
        }

        public bool Has(string text)
        {
            lock (_lines)
            {
                return _lines.Any(line => line.Contains(text, StringComparison.Ordinal));
            }
        }
    }
}
