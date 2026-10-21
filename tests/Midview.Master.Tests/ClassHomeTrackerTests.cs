using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassHomeTracker against loopback Agents that keep the class folders' home
// (ClassSyncRig's homes, real AgentServers): which teacher PC is home, and
// whether this Master works the folders itself (Local) or through that PC
// (Remote). Also the Agent messages it and the sync go by (ClassFolderClient).
public sealed class ClassHomeTrackerTests
{
    private const string ThisPc = ClassSyncRig.MachineName;
    private const string OtherPc = "PROF-2";

    private static ClassHomeTracker NewTracker(ClassSyncRig rig) =>
        new(rig.Master, ThisPc, () => rig.Machines, machine => !rig.Offline.ContainsKey(machine.Name),
            TimeSpan.FromHours(1), rig.AgentPort);

    // What a teacher PC's start sends each Agent first.
    private static void Claim(ClassSyncRig rig, StudentPc pc, X509Certificate2 master, string name, bool takeOver = false)
    {
        var (ok, detail, oldAgent) = ClassFolderClient.ClaimHome(
            pc.Machine, master, new ClassHomeClaim(name, ClassFolderRemote.Port, takeOver), rig.AgentPort);
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

    // Agents from before two teacher PCs say nothing about the home, not "no home".
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

    [Fact]
    public async Task TheHomeMostAgentsReport_Wins_AndOfflineAgentsAreNotAsked()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var tracker = NewTracker(rig);
        Claim(rig, rig.Pcs[0], rig.OtherMaster, OtherPc);
        Claim(rig, rig.Pcs[1], rig.OtherMaster, OtherPc);
        Claim(rig, rig.Pcs[2], rig.Master, ThisPc);

        await tracker.RefreshAsync();

        Assert.Equal(OtherPc, tracker.Home?.Name);
        Assert.Equal(ClassFolderMode.Remote, tracker.Mode);

        rig.Offline[rig.Pcs[0].Name] = true;
        rig.Offline[rig.Pcs[1].Name] = true;
        await tracker.RefreshAsync();

        Assert.Equal(ThisPc, tracker.Home?.Name);
        Assert.Equal(ClassFolderMode.Local, tracker.Mode);
    }

    // An Agent with no home is a vote for none, as it is when a start claims the home:
    // most Agents would take this PC as home, so its Master works the folders itself.
    [Fact]
    public async Task MostAgentsWithNoHome_IsNoHome()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var tracker = NewTracker(rig);
        Claim(rig, rig.Pcs[0], rig.OtherMaster, OtherPc);

        await tracker.RefreshAsync();

        Assert.Null(tracker.Home);
        Assert.Equal(ClassFolderMode.Local, tracker.Mode);
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

    // ---- ClassFolderClient: the Agent messages for the home ----

    [Fact]
    public void AnAgentFromBefore_SaysSoToTheHomesMessages()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        var machine = rig.Pcs[0].Machine;

        Assert.Equal((false, "", true), ClassFolderClient.ClaimHome(
            machine, rig.Master, new ClassHomeClaim(ThisPc, ClassFolderRemote.Port, false), rig.AgentPort));
        Assert.Equal(((ClassHome?)null, true), ClassFolderClient.GetHome(machine, rig.Master, rig.AgentPort));
    }

    [Fact]
    public void ClaimHome_NamingAnotherHome_IsRefusedWithItsName()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        var machine = rig.Pcs[0].Machine;
        Claim(rig, rig.Pcs[0], rig.OtherMaster, OtherPc);

        var (ok, detail, oldAgent) = ClassFolderClient.ClaimHome(
            machine, rig.Master, new ClassHomeClaim(ThisPc, ClassFolderRemote.Port, false), rig.AgentPort);
        var (home, homeFromOldAgent) = ClassFolderClient.GetHome(machine, rig.Master, rig.AgentPort);

        Assert.False(ok);
        Assert.Equal(ClassHomeClaim.OtherHomeText + OtherPc, detail);
        Assert.False(oldAgent);
        Assert.False(homeFromOldAgent);
        Assert.Equal(OtherPc, home?.Name);
        Assert.Equal(rig.OtherMaster.Thumbprint, home!.Thumbprint, ignoreCase: true);
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
