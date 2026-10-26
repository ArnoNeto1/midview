using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// ThumbnailPoller taking back a straggler: while this PC is
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
    [InlineData("PROF-2")] // another PC, older: taken back
    [InlineData(ClassSyncRig.MachineName)] // this PC at an older generation: brought up to it
    public async Task AnAgentKeepingAnOlderHome_IsClaimed_AsItsMachineAnswers(string keeps)
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        var pc = rig.Pcs[0];
        ClaimFor(rig, pc, keeps == ClassSyncRig.MachineName ? rig.Master : rig.OtherMaster, keeps, HomeGeneration - 1_000);
        using var poller = new ThumbnailPoller(pc.Machine, rig.Master, rig.SessionPort, rig.AgentPort, classHomeClaim: HomeClaim);

        poller.Start();

        await WaitUntil(() => pc.Agent.CurrentHome?.Generation == HomeGeneration);
        Assert.Equal(ClassSyncRig.MachineName, pc.Agent.CurrentHome!.Name);
        Assert.Equal(rig.Master.Thumbprint, pc.Agent.CurrentHome.Thumbprint, ignoreCase: true);
    }

    // Only an older home is claimed: one at least as new isn't even asked (the
    // Agent would refuse such a take-over anyway, which is why this counts the
    // claims that reach it rather than what it keeps afterwards).
    [Theory]
    [InlineData(HomeGeneration - 1_000, true)]
    [InlineData(HomeGeneration + 1_000, false)] // another PC, newer: this PC isn't the home any more
    [InlineData(HomeGeneration, false)]
    public async Task OnlyAnAgentKeepingAnOlderHome_IsSentAClaim(long at, bool claimed)
    {
        using var session = new LoopbackSession();
        using var agent = new HomeKeepingAgent(
            session.MasterCertificate, new ClassHome(new string('A', 40), "PROF-2", "127.0.0.1", ClassFolderRemote.Port, at));
        using var poller = new ThumbnailPoller(
            session.Machine() with { AgentThumbprint = agent.Thumbprint }, session.MasterCertificate, session.Port, agent.Port,
            agentRetryInterval: TimeSpan.Zero, classHomeClaim: HomeClaim);

        await ThreeThumbnails(poller);

        Assert.True(agent.HomesAsked > 0);
        if (claimed)
        {
            Assert.True(agent.Claims > 0);
        }
        else
        {
            Assert.Equal(0, agent.Claims);
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
    // (the folders never move mid-lesson): that PC is left in it, and taken back once the lesson is over.
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

    // The home's address changed (DHCP) while the Agents still keep the old
    // one: a colleague's PC finds nobody there. One poller cycle of the home
    // claims the Agent all the same (once per connection), which refreshes
    // the address and port it keeps, and the colleague's PC finds the home again.
    [Fact]
    public async Task AHomeWhoseAddressChanged_IsFoundByTheRemoteAgain_AfterOnePollerCycle()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        var pc = rig.Pcs[0];
        var sync = rig.NewSync();
        using var host = ClassFolderHost.Open(
            rig.Master, LocalClassFolderControllerTests.NewController(rig, sync),
            (thumbprint, _) => Task.FromResult(string.Equals(thumbprint, rig.OtherMaster.Thumbprint, StringComparison.OrdinalIgnoreCase)),
            0, _ => { }, IPAddress.Loopback) ?? throw new InvalidOperationException("The host could not listen.");
        var (ok, detail) = pc.Agent.ClaimHome(
            rig.Master.Thumbprint, "127.0.0.9", new ClassHomeClaim(ClassSyncRig.MachineName, host.Port + 1, false, HomeGeneration));
        Assert.True(ok, detail);
        using var tracker = new ClassHomeTracker(
            rig.OtherMaster, "PROF-2", () => rig.Machines, _ => true, TimeSpan.FromHours(1), rig.AgentPort,
            Path.Combine(rig.Root, "outro-professor", "class-home.json"));
        await tracker.RefreshAsync();
        Assert.Equal(("127.0.0.9", host.Port + 1), (tracker.Home?.Address, tracker.Home?.Port));
        var claim = new ClassHomeClaim(ClassSyncRig.MachineName, host.Port, TakeOver: true, HomeGeneration);

        using (var poller = new ThumbnailPoller(pc.Machine, rig.Master, rig.SessionPort, rig.AgentPort, classHomeClaim: () => claim))
        {
            await ThreeThumbnails(poller);
        }

        Assert.Equal(new ClassHome(rig.Master.Thumbprint.ToUpperInvariant(), ClassSyncRig.MachineName, "127.0.0.1", host.Port, HomeGeneration),
            pc.Agent.CurrentHome);
        await tracker.RefreshAsync();
        using var remote = new RemoteClassFolderController(
            rig.OtherMaster, () => tracker.Home, () => rig.Machines, _ => true, TimeSpan.FromHours(1), rig.AgentPort, () => rig.Now);
        var state = await remote.ReadAsync(lessonFiles: false);
        Assert.False(remote.HomeClosed);
        Assert.Equal(new[] { ClassSyncRig.Turma, "3º B" }, state.Classes.Select(summary => summary.Turma));
    }

    // Another Windows account on this PC (its own certificate, the same
    // computer name: the same PC to the Agent) runs a lesson: this account's
    // poller never claims that Agent meanwhile (the Agent would take the
    // claim as the home's own, and hand this account the home mid-lesson),
    // whether it keeps that home at an older generation or at this one. Once
    // the lesson ends, it does.
    [Theory]
    [InlineData(HomeGeneration - 1_000)]
    [InlineData(HomeGeneration)]
    public async Task AnAgentInALessonOfAnotherCertificateWithThisName_IsNotClaimed_UntilItEnds(long kept)
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        var pc = rig.Pcs[0];
        ClaimFor(rig, pc, rig.OtherMaster, ClassSyncRig.MachineName, kept);
        var now = new DateTimeOffset(rig.Now).ToUnixTimeSeconds();
        var lesson = new ClassLesson(now, Guid.NewGuid(), ClassSyncRig.Turma, now, Ended: false);
        SetLessonOn(rig, pc, lesson);
        using var poller = new ThumbnailPoller(
            pc.Machine, rig.Master, rig.SessionPort, rig.AgentPort, agentRetryInterval: TimeSpan.FromMilliseconds(200),
            classHomeClaim: HomeClaim);

        await ThreeThumbnails(poller);
        Assert.Equal(rig.OtherMaster.Thumbprint, pc.Agent.CurrentHome?.Thumbprint, ignoreCase: true);
        Assert.Equal(lesson, pc.Agent.Current);

        SetLessonOn(rig, pc, lesson with { SavedAt = now + 1, Ended = true });

        await WaitUntil(() => string.Equals(pc.Agent.CurrentHome?.Thumbprint, rig.Master.Thumbprint, StringComparison.OrdinalIgnoreCase));
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

    // An Agent that keeps the home it is given, counts the questions about it
    // and the claims that reach it (refusing them), and says yes to the rest.
    private sealed class HomeKeepingAgent : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly System.Security.Cryptography.X509Certificates.X509Certificate2 _certificate =
            SelfSignedCertificate.Create("agent-under-test");
        private int _homesAsked;
        private int _claims;

        public HomeKeepingAgent(System.Security.Cryptography.X509Certificates.X509Certificate2 masterCertificate, ClassHome home)
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            var masters = new TrustStore(new[] { masterCertificate.Thumbprint });
            new Thread(() =>
            {
                while (true)
                {
                    TcpClient client;
                    try
                    {
                        client = _listener.AcceptTcpClient();
                    }
                    catch (Exception)
                    {
                        return; // Dispose stopped the listener
                    }
                    using (client)
                    {
                        try
                        {
                            using var channel = SecureChannel.AuthenticateAsServer(client, _certificate, masters);
                            var message = channel.Receive();
                            if (message.Type == MessageType.GetClassHome)
                            {
                                Interlocked.Increment(ref _homesAsked);
                                channel.Send(new Message(MessageType.ClassHomeStatus, home.Encode()));
                            }
                            else if (message.Type == MessageType.ClaimClassHome)
                            {
                                Interlocked.Increment(ref _claims);
                                channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(false, home.RefusalText)));
                            }
                            else
                            {
                                channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(true, string.Empty)));
                            }
                        }
                        catch (Exception)
                        {
                            // The poller gave up on this connection.
                        }
                    }
                }
            }) { IsBackground = true }.Start();
        }

        public int Port { get; }

        public string Thumbprint => _certificate.Thumbprint;

        public int HomesAsked => Volatile.Read(ref _homesAsked);

        public int Claims => Volatile.Read(ref _claims);

        public void Dispose()
        {
            _listener.Stop();
            _certificate.Dispose();
        }
    }
}
