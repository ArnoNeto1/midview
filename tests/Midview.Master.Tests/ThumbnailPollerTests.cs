using System.Net;
using System.Net.Sockets;
using Midview.Master;
using Midview.Protocol;
using Midview.Session;
using Xunit;

public partial class ThumbnailPollerTests
{
    [Fact]
    public async Task SessionRunning_ReceivesAThumbnailJpeg()
    {
        using var session = new LoopbackSession();
        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var poller = new ThumbnailPoller(session.Machine(), session.MasterCertificate, session.Port);
        poller.ThumbnailReceived += jpeg => received.TrySetResult(jpeg);
        poller.Start();

        var jpeg = await received.Task.WaitAsync(TimeSpan.FromSeconds(30));

        using var thumbnail = TileCodec.DecodeTile(jpeg);
        Assert.Equal(ThumbnailRenderer.MaxWidth, thumbnail.Width);
    }

    [Fact]
    public async Task AskedForASize_ReceivesThatSize_AndFollowsItWhenItChanges()
    {
        using var session = new LoopbackSession();
        var size = (Width: 160, Height: 90);
        var sizes = new List<(int, int)>();
        var secondThumbnail = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var poller = new ThumbnailPoller(
            session.Machine(), session.MasterCertificate, session.Port, thumbnailSize: () =>
            {
                lock (sizes)
                {
                    return size;
                }
            });
        poller.ThumbnailReceived += jpeg =>
        {
            using var thumbnail = TileCodec.DecodeTile(jpeg);
            lock (sizes)
            {
                sizes.Add((thumbnail.Width, thumbnail.Height));
                if (sizes.Count == 1)
                {
                    // The teacher made the tiles bigger.
                    size = (640, 360);
                }
                else
                {
                    secondThumbnail.TrySetResult();
                }
            }
        };
        poller.Start();

        await secondThumbnail.Task.WaitAsync(ThumbnailPoller.PollInterval + TimeSpan.FromSeconds(30));

        lock (sizes)
        {
            Assert.Equal((160, 90), sizes[0]);
            Assert.Equal((640, 360), sizes[1]);
        }
    }

    [Fact]
    public async Task SessionRunning_KeepsPolling()
    {
        using var session = new LoopbackSession();
        var count = 0;
        var secondThumbnail = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var poller = new ThumbnailPoller(session.Machine(), session.MasterCertificate, session.Port);
        poller.ThumbnailReceived += _ =>
        {
            if (Interlocked.Increment(ref count) == 2)
            {
                secondThumbnail.TrySetResult();
            }
        };
        poller.Start();

        await secondThumbnail.Task.WaitAsync(ThumbnailPoller.PollInterval + TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task WrongSessionThumbprint_ReportsTheCertificateAsRefused()
    {
        using var session = new LoopbackSession();
        var reason = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var poller = new ThumbnailPoller(
            session.Machine() with { SessionThumbprint = "0000000000000000000000000000000000000000" },
            session.MasterCertificate, session.Port);
        poller.Unreachable += r => reason.TrySetResult(r);
        poller.Start();

        Assert.Equal("Certificado recusado", await reason.Task.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task NothingListening_ReportsNobodyLoggedIn()
    {
        var port = FreePort(); // nothing listens on it

        using var masterCertificate = SelfSignedCertificate.Create("master-under-test");
        var reason = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var poller = new ThumbnailPoller(
            new KnownMachine("Aluno-1", "127.0.0.1", "ANY", null), masterCertificate, port);
        poller.Unreachable += r => reason.TrySetResult(r);
        poller.Start();

        Assert.Equal("Sem usuário logado", await reason.Task.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task MachineComesOnlineLater_PollerRecoversOnItsOwn()
    {
        var port = FreePort();
        using var masterCertificate = SelfSignedCertificate.Create("master-under-test");
        using var sessionCertificate = SelfSignedCertificate.Create("session-under-test");
        var wentUnreachable = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var poller = new ThumbnailPoller(
            new KnownMachine("Aluno-1", "127.0.0.1", sessionCertificate.Thumbprint, null), masterCertificate, port);
        poller.Unreachable += _ => wentUnreachable.TrySetResult();
        poller.ThumbnailReceived += _ => received.TrySetResult();
        poller.Start();
        await wentUnreachable.Task.WaitAsync(TimeSpan.FromSeconds(30));

        using var session = new LoopbackSession(port, sessionCertificate, masterCertificate);

        await received.Task.WaitAsync(ThumbnailPoller.PollInterval + TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task SessionRunning_LearnsTheMacAddressFromTheAgent()
    {
        using var session = new LoopbackSession();
        using var agent = new FakeAgent(session.MasterCertificate,
            new Message(MessageType.WakeAddress, new byte[] { 0x00, 0x1A, 0x2B, 0x3C, 0x4D, 0x5E }));
        var learned = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var poller = new ThumbnailPoller(
            session.Machine() with { AgentThumbprint = agent.Thumbprint }, session.MasterCertificate, session.Port, agent.Port,
            agentRetryInterval: RetryAtOnce);
        poller.MacAddressLearned += macAddress => learned.TrySetResult(macAddress);
        poller.Start();

        Assert.Equal("00-1A-2B-3C-4D-5E", await learned.Task.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task AgentFromBeforeWakeOnLan_IsAskedOnce_AndNothingIsLearned()
    {
        using var session = new LoopbackSession();
        using var agent = new FakeAgent(session.MasterCertificate, new Message(MessageType.CommandAck, new byte[] { 0 }));
        var learned = false;
        using var poller = new ThumbnailPoller(
            session.Machine() with { AgentThumbprint = agent.Thumbprint }, session.MasterCertificate, session.Port, agent.Port,
            agentRetryInterval: RetryAtOnce);
        poller.MacAddressLearned += _ => learned = true;
        var polls = new Polls(poller);
        poller.Start();

        await WaitUntil(() => agent.Questions > 0);
        await polls.OneMoreAsync();

        Assert.Equal(1, agent.Questions);
        Assert.False(learned);
    }

    [Fact]
    public async Task NoAgentThumbprint_TheAgentIsNeverAsked()
    {
        using var session = new LoopbackSession();
        using var agent = new FakeAgent(session.MasterCertificate, new Message(MessageType.WakeAddress, new byte[6]));
        using var poller = new ThumbnailPoller(session.Machine(), session.MasterCertificate, session.Port, agent.Port);

        await ThreeThumbnails(poller);

        Assert.Equal(0, agent.Questions);
    }

    [Fact]
    public async Task PowerSchedule_IsHandedToTheAgentOnce_AndAgainWhenItChanges()
    {
        using var session = new LoopbackSession();
        using var agent = new FakeAgent(session.MasterCertificate, new Message(MessageType.CommandAck, CommandResult.Encode(true, "ok")));
        var schedule = new PowerSchedule(1000, PowerSchedule.Weekdays, null, 18 * 60);
        using var poller = new ThumbnailPoller(
            session.Machine() with { AgentThumbprint = agent.Thumbprint }, session.MasterCertificate, session.Port, agent.Port,
            () => schedule, agentRetryInterval: RetryAtOnce);
        var polls = new Polls(poller);
        poller.Start();

        await WaitUntil(() => agent.SchedulesReceived.Length > 0);
        await polls.OneMoreAsync();
        Assert.Equal(new[] { 1000L }, agent.SchedulesReceived);

        schedule = schedule with { SavedAt = 2000 };
        await WaitUntil(() => agent.SchedulesReceived.Length > 1);
        await polls.OneMoreAsync();
        Assert.Equal(new[] { 1000L, 2000L }, agent.SchedulesReceived);
    }

    [Fact]
    public async Task AppBlock_IsHandedToTheAgentOnce_AndAgainWhenItChanges_IncludingALift()
    {
        using var session = new LoopbackSession();
        using var agent = new FakeAgent(session.MasterCertificate, new Message(MessageType.CommandAck, CommandResult.Encode(true, "ok")));
        var policy = new AppBlockPolicy(1000, AppBlockMode.BlockList, new[] { "chrome.exe" });
        using var poller = new ThumbnailPoller(
            session.Machine() with { AgentThumbprint = agent.Thumbprint }, session.MasterCertificate, session.Port, agent.Port,
            appBlockPolicy: () => policy, agentRetryInterval: RetryAtOnce);
        var polls = new Polls(poller);
        poller.Start();

        await WaitUntil(() => agent.BlocksReceived.Length > 0);
        await polls.OneMoreAsync();
        Assert.Equal(new[] { 1000L }, agent.BlocksReceived);

        // Lifting is a newer policy like any other: a machine that was off
        // when it was lifted is freed the same way.
        policy = AppBlockPolicy.Lifted(2000);
        await WaitUntil(() => agent.BlocksReceived.Length > 1);
        await polls.OneMoreAsync();
        Assert.Equal(new[] { 1000L, 2000L }, agent.BlocksReceived);
    }

    [Fact]
    public async Task AppBlock_AnAgentThatDidNotTakeIt_IsNotMarkedDelivered_AndIsTriedAgain()
    {
        using var session = new LoopbackSession();
        // An older Agent answers with a bare ack, which reads as "no".
        using var agent = new FakeAgent(session.MasterCertificate, new Message(MessageType.CommandAck, CommandResult.Encode(false, string.Empty)));
        var policy = new AppBlockPolicy(1000, AppBlockMode.BlockList, new[] { "chrome.exe" });
        using var poller = new ThumbnailPoller(
            session.Machine() with { AgentThumbprint = agent.Thumbprint }, session.MasterCertificate, session.Port, agent.Port,
            appBlockPolicy: () => policy, agentRetryInterval: TimeSpan.Zero);

        poller.Start();

        // Sent again: an Agent that said no isn't taken to have it.
        await WaitUntil(() => agent.BlocksReceived.Length >= 2);
    }

    [Fact]
    public async Task AppBlockObserved_ReportsWhatTheAgentFollows()
    {
        using var session = new LoopbackSession();
        var followed = new AppBlockPolicy(1000, AppBlockMode.AllowOnly, new[] { "winword.exe" });
        using var agent = new FakeAgent(session.MasterCertificate, new Message(MessageType.CommandAck, CommandResult.Encode(true, "ok")))
        {
            StatusReply = new Message(MessageType.AppBlockStatus, followed.Encode()),
        };
        using var poller = new ThumbnailPoller(
            session.Machine() with { AgentThumbprint = agent.Thumbprint }, session.MasterCertificate, session.Port, agent.Port);
        var observed = new TaskCompletionSource<AppBlockClient.StatusResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        poller.AppBlockObserved += status => SetIfAnswered(status, observed);

        poller.Start();
        var status = await observed.Task.WaitAsync(ObservedWithin);

        Assert.False(status.OldAgent);
        Assert.True(followed.SameRulesAs(status.Policy!));
    }

    [Fact]
    public async Task AppBlockObserved_AnAgentFromBeforeTheFeature_IsReportedAsOld()
    {
        using var session = new LoopbackSession();
        using var agent = new FakeAgent(session.MasterCertificate, new Message(MessageType.CommandAck, CommandResult.Encode(true, string.Empty)));
        using var poller = new ThumbnailPoller(
            session.Machine() with { AgentThumbprint = agent.Thumbprint }, session.MasterCertificate, session.Port, agent.Port);
        var observed = new TaskCompletionSource<AppBlockClient.StatusResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        poller.AppBlockObserved += status => SetIfAnswered(status, observed);

        poller.Start();
        var status = await observed.Task.WaitAsync(ObservedWithin);

        Assert.True(status.OldAgent);
    }

    // An observation the Agent didn't answer (null: on a loaded test run its
    // connect can time out) is followed by the next one 30 s later
    // (AppBlockObserveInterval); the first it answers is the one these tests
    // look at, within two observations' time and some.
    private static readonly TimeSpan ObservedWithin = TimeSpan.FromSeconds(90);

    private static void SetIfAnswered(AppBlockClient.StatusResult? status, TaskCompletionSource<AppBlockClient.StatusResult> observed)
    {
        if (status is not null)
        {
            observed.TrySetResult(status);
        }
    }


    [Fact]
    public async Task ResendsTheClassLesson_WhenTheAgentComesBack()
    {
        using var session = new LoopbackSession();
        using var agentCertificate = SelfSignedCertificate.Create("agent-under-test");
        var agentPort = FreePort();
        var lesson = new ClassLesson(1000, Guid.NewGuid(), "3º A", 1, Ended: false);
        using var poller = new ThumbnailPoller(
            session.Machine() with { AgentThumbprint = agentCertificate.Thumbprint }, session.MasterCertificate, session.Port,
            agentPort, agentRetryInterval: TimeSpan.Zero, classLesson: () => lesson);

        // The Agent isn't running when the lesson starts: the poller tries, and goes on polling.
        var thumbnails = 0;
        poller.ThumbnailReceived += _ => Interlocked.Increment(ref thumbnails);
        poller.Start();
        await WaitUntil(() => Volatile.Read(ref thumbnails) >= 2);

        using var agent = new FakeAgent(
            session.MasterCertificate, new Message(MessageType.CommandAck, CommandResult.Encode(true, "aula do 3º A")),
            agentPort, agentCertificate);
        await WaitUntil(() => agent.LessonsReceived.Length > 0);
        Assert.Equal(new[] { 1000L }, agent.LessonsReceived);

        // Delivered once; a change (the end of the lesson) goes too.
        lesson = lesson with { SavedAt = 1001, Ended = true };
        await WaitUntil(() => agent.LessonsReceived.Length > 1);
        Assert.Equal(new[] { 1000L, 1001L }, agent.LessonsReceived);
    }

    // No lesson at all (a Master that stood down) is never handed over, however
    // it is saved: an Agent would take it for a newer end of the lesson it follows.
    [Fact]
    public async Task NoLesson_IsNeverHandedOver()
    {
        using var session = new LoopbackSession();
        using var agentCertificate = SelfSignedCertificate.Create("agent-under-test");
        var agentPort = FreePort();
        var lesson = ClassLesson.None(5000);
        using var agent = new FakeAgent(
            session.MasterCertificate, new Message(MessageType.CommandAck, CommandResult.Encode(true, "aula encerrada")),
            agentPort, agentCertificate);
        using var poller = new ThumbnailPoller(
            session.Machine() with { AgentThumbprint = agentCertificate.Thumbprint }, session.MasterCertificate, session.Port,
            agentPort, agentRetryInterval: TimeSpan.Zero, classLesson: () => lesson);
        var thumbnails = 0;
        poller.ThumbnailReceived += _ => Interlocked.Increment(ref thumbnails);

        poller.Start();
        await WaitUntil(() => Volatile.Read(ref thumbnails) >= 2);
        Assert.Empty(agent.LessonsReceived);

        lesson = new ClassLesson(5001, Guid.NewGuid(), "3º A", 1, Ended: false);
        await WaitUntil(() => agent.LessonsReceived.Length > 0);
        Assert.Equal(new[] { 5001L }, agent.LessonsReceived);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + 6 * ThumbnailPoller.PollInterval + TimeSpan.FromSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out");
            await Task.Delay(50);
        }
    }

    private static async Task ThreeThumbnails(ThumbnailPoller poller)
    {
        var count = 0;
        var third = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        poller.ThumbnailReceived += _ =>
        {
            if (Interlocked.Increment(ref count) == 3)
            {
                third.TrySetResult();
            }
        };
        poller.Start();
        await third.Task.WaitAsync(2 * ThumbnailPoller.PollInterval + TimeSpan.FromSeconds(30));
    }

    // For a test that waits for an Agent chore to be done: one a loaded test
    // run made fail (its connect or its answer timed out) is tried again at
    // the next poll, not DefaultAgentRetryInterval later. Never for a test of
    // what waits that interval.
    private static readonly TimeSpan RetryAtOnce = TimeSpan.Zero;

    // Counts a poller's thumbnails (made before Start). A poll's Agent chores
    // come right after its thumbnail, so two thumbnails more than now mean
    // that at least one whole poll more has done its chores.
    private sealed class Polls
    {
        private int _thumbnails;

        public Polls(ThumbnailPoller poller) => poller.ThumbnailReceived += _ => Interlocked.Increment(ref _thumbnails);

        public Task OneMoreAsync()
        {
            var seen = Volatile.Read(ref _thumbnails);
            return WaitUntil(() => Volatile.Read(ref _thumbnails) >= seen + 2);
        }
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
