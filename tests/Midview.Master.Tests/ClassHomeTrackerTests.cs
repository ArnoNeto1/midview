using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassHomeTracker against loopback Agents that keep the class folders' home
// (ClassSyncRig's homes, real AgentServers): which teacher PC is home, and
// whether this Master works the folders itself (Local) or through that PC
// (Remote). Also the Agent messages it and the sync go by (ClassFolderClient),
// and this PC's generation as the home (ClassHomeFileMaster), in temp files.
public sealed class ClassHomeTrackerTests
{
    private const string ThisPc = ClassSyncRig.MachineName;
    private const string OtherPc = "PROF-2";

    // When a PC became the home (ClassHome.Generation).
    private const long Older = 1_000;
    private const long Newer = 2_000;

    private static ClassHomeTracker NewTracker(ClassSyncRig rig) =>
        new(rig.Master, ThisPc, () => rig.Machines, machine => !rig.Offline.ContainsKey(machine.Name),
            TimeSpan.FromHours(1), rig.AgentPort, rig.HomePath);

    // What a teacher PC's start sends each Agent first.
    private static void Claim(
        ClassSyncRig rig, StudentPc pc, X509Certificate2 master, string name, long generation = Older, bool takeOver = false)
    {
        var (ok, detail, oldAgent) = ClassFolderClient.ClaimHome(
            pc.Machine, master, new ClassHomeClaim(name, ClassFolderRemote.Port, takeOver, generation), rig.AgentPort);
        Assert.True(ok, detail);
        Assert.False(oldAgent);
    }

    [Fact]
    public async Task NoHome_IsLocal()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var tracker = NewTracker(rig);
        var changes = 0;
        tracker.Changed += () => Interlocked.Increment(ref changes);

        await tracker.RefreshAsync();

        Assert.Null(tracker.Home);
        Assert.Equal(ClassFolderMode.Local, tracker.Mode);
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task ThisPcAsHome_IsLocal_AndChangedIsRaisedOnce()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var tracker = NewTracker(rig);
        var changes = 0;
        tracker.Changed += () => Interlocked.Increment(ref changes);
        foreach (var pc in rig.Pcs)
        {
            Claim(rig, pc, rig.Master, ThisPc);
        }

        await tracker.RefreshAsync();
        await tracker.RefreshAsync();

        Assert.NotNull(tracker.Home);
        Assert.Equal(ThisPc, tracker.Home!.Name);
        Assert.Equal(rig.Master.Thumbprint, tracker.Home.Thumbprint, ignoreCase: true);
        Assert.Equal(ClassFolderRemote.Port, tracker.Home.Port);
        Assert.Equal(Older, tracker.Home.Generation);
        Assert.Equal(ClassFolderMode.Local, tracker.Mode);
        Assert.Equal(1, changes);
    }

    // Two teacher PCs can share one teacher identity: the computer name tells them apart.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnotherPcAsHome_IsRemote(bool sameCertificate)
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var tracker = NewTracker(rig);
        var changes = 0;
        tracker.Changed += () => Interlocked.Increment(ref changes);
        foreach (var pc in rig.Pcs)
        {
            Claim(rig, pc, sameCertificate ? rig.Master : rig.OtherMaster, OtherPc);
        }

        await tracker.RefreshAsync();

        Assert.Equal(OtherPc, tracker.Home?.Name);
        Assert.Equal(ClassFolderMode.Remote, tracker.Mode);
        Assert.Equal(1, changes);
    }

    // Midview reinstalled on this PC (a new certificate): the Agents still know it by its name.
    [Fact]
    public async Task ThisPcsName_UnderAnotherCertificate_IsStillThisPc()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var tracker = NewTracker(rig);
        foreach (var pc in rig.Pcs)
        {
            Claim(rig, pc, rig.OtherMaster, ThisPc);
        }

        await tracker.RefreshAsync();

        Assert.Equal(ThisPc, tracker.Home?.Name);
        Assert.Equal(ClassFolderMode.Local, tracker.Mode);
    }

    // This PC renamed: its certificate and the generation it keeps on file.
    [Fact]
    public async Task ThisPcRenamed_IsStillThisPc_ByItsGeneration()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var tracker = NewTracker(rig);
        foreach (var pc in rig.Pcs)
        {
            Claim(rig, pc, rig.Master, "PROF-ANTIGO");
        }

        await tracker.RefreshAsync();
        Assert.Equal(ClassFolderMode.Remote, tracker.Mode); // without the generation, another PC with this certificate

        ClassHomeFileMaster.Write(Older, rig.HomePath);
        await tracker.RefreshAsync();

        Assert.Equal("PROF-ANTIGO", tracker.Home?.Name);
        Assert.Equal(ClassFolderMode.Local, tracker.Mode);
    }

    [Fact]
    public async Task OldAgents_AreIgnored()
    {
        using var rig = new ClassSyncRig(homes: 1);
        using var tracker = NewTracker(rig);
        Claim(rig, rig.Pcs[0], rig.OtherMaster, OtherPc);

        await tracker.RefreshAsync();

        Assert.Equal(OtherPc, tracker.Home?.Name);
        Assert.Equal(ClassFolderMode.Remote, tracker.Mode);
    }

    // Ruling 9: the newest home wins, however few Agents have heard of it yet;
    // an Agent that isn't online, or doesn't answer, says nothing.
    [Fact]
    public async Task TheHomeOfTheNewestGeneration_Wins_AndOnlyAnsweringAgentsCount()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var tracker = NewTracker(rig);
        Claim(rig, rig.Pcs[0], rig.OtherMaster, OtherPc, Older);
        Claim(rig, rig.Pcs[1], rig.OtherMaster, OtherPc, Older);
        Claim(rig, rig.Pcs[2], rig.Master, ThisPc, Newer);

        await tracker.RefreshAsync();

        Assert.Equal(ThisPc, tracker.Home?.Name);
        Assert.Equal(ClassFolderMode.Local, tracker.Mode);

        rig.Offline[rig.Pcs[2].Name] = true;
        await tracker.RefreshAsync();
        Assert.Equal(OtherPc, tracker.Home?.Name);
        Assert.Equal(ClassFolderMode.Remote, tracker.Mode);

        rig.Offline.TryRemove(rig.Pcs[2].Name, out _);
        await tracker.RefreshAsync();
        Assert.Equal(ClassFolderMode.Local, tracker.Mode);

        rig.Pcs[2].StopAgent();
        await tracker.RefreshAsync();
        Assert.Equal(OtherPc, tracker.Home?.Name);
        Assert.Equal(ClassFolderMode.Remote, tracker.Mode);
    }

    // Agents with no home don't outvote one that has one.
    [Fact]
    public async Task AHomeOnlyOneAgentReports_IsTheHome()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var tracker = NewTracker(rig);
        Claim(rig, rig.Pcs[0], rig.OtherMaster, OtherPc);

        await tracker.RefreshAsync();

        Assert.Equal(OtherPc, tracker.Home?.Name);
        Assert.Equal(ClassFolderMode.Remote, tracker.Mode);
    }

    // Two PCs of the same generation: the one most Agents report, and on a tie the one the
    // tracker already has, whichever that is.
    [Fact]
    public async Task ATie_KeepsTheCurrentHome()
    {
        using var rig = new ClassSyncRig(homes: 3);
        Claim(rig, rig.Pcs[0], rig.OtherMaster, OtherPc, Older);
        Claim(rig, rig.Pcs[1], rig.OtherMaster, OtherPc, Older);
        Claim(rig, rig.Pcs[2], rig.Master, "PROF-3", Older);

        using (var tracker = NewTracker(rig))
        {
            await tracker.RefreshAsync();
            Assert.Equal(OtherPc, tracker.Home?.Name);

            rig.Offline[rig.Pcs[0].Name] = true; // one each now
            for (var refresh = 0; refresh < 5; refresh++)
            {
                await tracker.RefreshAsync();
                Assert.Equal(OtherPc, tracker.Home?.Name);
            }
        }

        rig.Offline[rig.Pcs[1].Name] = true;
        using (var tracker = NewTracker(rig))
        {
            await tracker.RefreshAsync();
            Assert.Equal("PROF-3", tracker.Home?.Name);

            rig.Offline.TryRemove(rig.Pcs[1].Name, out _); // one each now
            for (var refresh = 0; refresh < 5; refresh++)
            {
                await tracker.RefreshAsync();
                Assert.Equal("PROF-3", tracker.Home?.Name);
            }
        }
    }

    [Fact]
    public async Task NoAgentOnline_IsLocal()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var tracker = NewTracker(rig);
        foreach (var pc in rig.Pcs)
        {
            Claim(rig, pc, rig.OtherMaster, OtherPc);
        }
        await tracker.RefreshAsync();
        Assert.Equal(ClassFolderMode.Remote, tracker.Mode);

        foreach (var pc in rig.Pcs)
        {
            rig.Offline[pc.Name] = true;
        }
        await tracker.RefreshAsync();

        Assert.Null(tracker.Home);
        Assert.Equal(ClassFolderMode.Local, tracker.Mode);
    }

    // ---- ClassHomeFileMaster: this PC's generation ----

    [Fact]
    public void TheGeneration_IsKeptOnFile_AndAnythingElseIsNone()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"midview-generation-{Guid.NewGuid():N}");
        var path = Path.Combine(folder, "class-home.json");
        try
        {
            Assert.Null(ClassHomeFileMaster.Read(path));

            ClassHomeFileMaster.Write(Newer, path);
            Assert.Equal(Newer, ClassHomeFileMaster.Read(path));
            Assert.False(File.Exists(path + ".tmp"));

            File.WriteAllText(path, "{ isto não é");
            Assert.Null(ClassHomeFileMaster.Read(path));
            File.WriteAllText(path, "{\"Generation\":0}");
            Assert.Null(ClassHomeFileMaster.Read(path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TakeOverGeneration_IsNewerThanEveryHome_AndNotBeforeNow()
    {
        var homes = new[]
        {
            new ClassHome(new string('A', 40), "PROF-1", "10.0.0.5", ClassFolderRemote.Port, Older),
            new ClassHome(new string('B', 40), "PROF-2", "10.0.0.6", ClassFolderRemote.Port, Newer),
        };

        Assert.Equal(Newer + 1, ClassHomeFileMaster.TakeOverGeneration(500, homes));
        Assert.Equal(9_000, ClassHomeFileMaster.TakeOverGeneration(9_000, homes));
        Assert.Equal(9_000, ClassHomeFileMaster.TakeOverGeneration(9_000, Array.Empty<ClassHome>()));
    }

    // ---- ClassFolderClient: the Agent messages for the home ----

    [Fact]
    public void AnAgentFromBefore_SaysSoToTheHomesMessages()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        var machine = rig.Pcs[0].Machine;

        Assert.Equal((false, "", true), ClassFolderClient.ClaimHome(
            machine, rig.Master, new ClassHomeClaim(ThisPc, ClassFolderRemote.Port, false, Older), rig.AgentPort));
        Assert.Equal(((ClassHome?)null, true), ClassFolderClient.GetHome(machine, rig.Master, rig.AgentPort));
    }

    [Fact]
    public void ClaimHome_NamingAnotherHome_IsRefusedWithItsName()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        var machine = rig.Pcs[0].Machine;
        Claim(rig, rig.Pcs[0], rig.OtherMaster, OtherPc);

        var (ok, detail, oldAgent) = ClassFolderClient.ClaimHome(
            machine, rig.Master, new ClassHomeClaim(ThisPc, ClassFolderRemote.Port, false, Newer), rig.AgentPort);
        var (home, homeFromOldAgent) = ClassFolderClient.GetHome(machine, rig.Master, rig.AgentPort);

        Assert.False(ok);
        Assert.Equal(ClassHomeClaim.OtherHomeText + OtherPc, detail);
        Assert.False(oldAgent);
        Assert.False(homeFromOldAgent);
        Assert.Equal(OtherPc, home?.Name);
        Assert.Equal(rig.OtherMaster.Thumbprint, home!.Thumbprint, ignoreCase: true);
        Assert.Equal(Older, home.Generation);
    }

    [Fact]
    public void GetLesson_ReadsTheAgentsLesson()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        var pc = rig.Pcs[0];
        var lesson = new ClassLesson(100, Guid.NewGuid(), ClassSyncRig.Turma, new DateTimeOffset(rig.Now).ToUnixTimeSeconds(), false);
        Assert.True(ClassFolderClient.SetLesson(pc.Machine, rig.Master, lesson, rig.AgentPort).Succeeded);

        Assert.Equal((lesson, false), ClassFolderClient.GetLesson(pc.Machine, rig.Master, rig.AgentPort));
    }

    [Fact]
    public void IsTeacherTrusted_AsksTheAgent()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        var machine = rig.Pcs[0].Machine;
        using var stranger = SelfSignedCertificate.Create("stranger");

        Assert.True(ClassFolderClient.IsTeacherTrusted(machine, rig.Master, rig.OtherMaster.Thumbprint, rig.AgentPort));
        Assert.False(ClassFolderClient.IsTeacherTrusted(machine, rig.Master, stranger.Thumbprint, rig.AgentPort));
        // Not a thumbprint: the Agent refuses the question, which is no answer either way.
        Assert.Null(ClassFolderClient.IsTeacherTrusted(machine, rig.Master, "não é uma impressão digital", rig.AgentPort));
    }
}
