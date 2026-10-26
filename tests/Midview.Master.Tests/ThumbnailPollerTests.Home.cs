using System.Net;
using System.Net.Sockets;
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
