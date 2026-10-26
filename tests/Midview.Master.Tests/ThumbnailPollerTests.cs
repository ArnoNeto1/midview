using System.Drawing;
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

        var jpeg = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));

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

        await secondThumbnail.Task.WaitAsync(ThumbnailPoller.PollInterval + TimeSpan.FromSeconds(10));

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

        await secondThumbnail.Task.WaitAsync(ThumbnailPoller.PollInterval + TimeSpan.FromSeconds(10));
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

        Assert.Equal("Certificado recusado", await reason.Task.WaitAsync(TimeSpan.FromSeconds(10)));
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

        Assert.Equal("Sem usuário logado", await reason.Task.WaitAsync(TimeSpan.FromSeconds(10)));
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
        await wentUnreachable.Task.WaitAsync(TimeSpan.FromSeconds(10));

        using var session = new LoopbackSession(port, sessionCertificate, masterCertificate);

        await received.Task.WaitAsync(ThumbnailPoller.PollInterval + TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task SessionRunning_LearnsTheMacAddressFromTheAgent()
    {
        using var session = new LoopbackSession();
        using var agent = new FakeAgent(session.MasterCertificate,
            new Message(MessageType.WakeAddress, new byte[] { 0x00, 0x1A, 0x2B, 0x3C, 0x4D, 0x5E }));
        var learned = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var poller = new ThumbnailPoller(
            session.Machine() with { AgentThumbprint = agent.Thumbprint }, session.MasterCertificate, session.Port, agent.Port);
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
            session.Machine() with { AgentThumbprint = agent.Thumbprint }, session.MasterCertificate, session.Port, agent.Port);
        poller.MacAddressLearned += _ => learned = true;

        await ThreeThumbnails(poller);

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
            () => schedule);
        var count = 0;
        var third = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sixth = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        poller.ThumbnailReceived += _ =>
        {
            var seen = Interlocked.Increment(ref count);
            if (seen == 3)
            {
                third.TrySetResult();
            }
            else if (seen == 6)
            {
                sixth.TrySetResult();
            }
        };
        poller.Start();
        var timeout = 3 * ThumbnailPoller.PollInterval + TimeSpan.FromSeconds(30);

        await third.Task.WaitAsync(timeout);
        Assert.Equal(new[] { 1000L }, agent.SchedulesReceived);

        schedule = schedule with { SavedAt = 2000 };
        await sixth.Task.WaitAsync(timeout);
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
            appBlockPolicy: () => policy);
        var count = 0;
        var third = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sixth = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        poller.ThumbnailReceived += _ =>
        {
            var seen = Interlocked.Increment(ref count);
            if (seen == 3)
            {
                third.TrySetResult();
            }
            else if (seen == 6)
            {
                sixth.TrySetResult();
            }
        };
        poller.Start();
        var timeout = 3 * ThumbnailPoller.PollInterval + TimeSpan.FromSeconds(30);

        await third.Task.WaitAsync(timeout);
        Assert.Equal(new[] { 1000L }, agent.BlocksReceived);

        // Lifting is a newer policy like any other: a machine that was off
        // when it was lifted is freed the same way.
        policy = AppBlockPolicy.Lifted(2000);
        await sixth.Task.WaitAsync(timeout);
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

        await ThreeThumbnails(poller);

        Assert.True(agent.BlocksReceived.Length >= 2, $"expected the block to be sent again, got {agent.BlocksReceived.Length}");
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
        var observed = new TaskCompletionSource<AppBlockClient.StatusResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        poller.AppBlockObserved += status => observed.TrySetResult(status);

        poller.Start();
        var status = await observed.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(status);
        Assert.False(status!.OldAgent);
        Assert.True(followed.SameRulesAs(status.Policy!));
    }

    [Fact]
    public async Task AppBlockObserved_AnAgentFromBeforeTheFeature_IsReportedAsOld()
    {
        using var session = new LoopbackSession();
        using var agent = new FakeAgent(session.MasterCertificate, new Message(MessageType.CommandAck, CommandResult.Encode(true, string.Empty)));
        using var poller = new ThumbnailPoller(
            session.Machine() with { AgentThumbprint = agent.Thumbprint }, session.MasterCertificate, session.Port, agent.Port);
        var observed = new TaskCompletionSource<AppBlockClient.StatusResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        poller.AppBlockObserved += status => observed.TrySetResult(status);

        poller.Start();
        var status = await observed.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(status);
        Assert.True(status!.OldAgent);
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

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    // A real SessionServer on a loopback port, with a fixed-size fake screen.
    internal sealed class LoopbackSession : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly System.Security.Cryptography.X509Certificates.X509Certificate2 _sessionCertificate;
        private readonly bool _ownsCertificates;

        public LoopbackSession()
            : this(0, SelfSignedCertificate.Create("session-under-test"), SelfSignedCertificate.Create("master-under-test"))
        {
            _ownsCertificates = true;
        }

        public LoopbackSession(
            int port,
            System.Security.Cryptography.X509Certificates.X509Certificate2 sessionCertificate,
            System.Security.Cryptography.X509Certificates.X509Certificate2 masterCertificate,
            IMessageDisplay? messageDisplay = null,
            IChatWindows? chatWindows = null,
            FileInbox? fileInbox = null,
            IAppCloser? appCloser = null,
            DeliveryFolder? deliveryFolder = null,
            IScreenLockDisplay? screenLock = null,
            ClassFolder? classFolder = null,
            IPAddress? address = null)
        {
            _listener = new TcpListener(address ?? IPAddress.Loopback, port);
            _sessionCertificate = sessionCertificate;
            MasterCertificate = masterCertificate;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            var server = new SessionServer(
                _listener, _sessionCertificate, new TrustStore(new[] { MasterCertificate.Thumbprint }),
                new FixedSizeCapture(), new NoInput(),
                new InputLockManager(new NoDevices(), new SystemClock(), TimeSpan.FromSeconds(5)),
                messageDisplay, chatWindows, fileInbox, appCloser, deliveryFolder, screenLock: screenLock, classFolder: classFolder);
            Task.Run(() =>
            {
                try
                {
                    server.Run(CancellationToken.None);
                }
                catch (SocketException)
                {
                    // Expected: Dispose stops the listener under AcceptTcpClient().
                }
            });
        }

        public System.Security.Cryptography.X509Certificates.X509Certificate2 MasterCertificate { get; }

        public int Port { get; }

        public KnownMachine Machine() => new("Aluno-1", "127.0.0.1", _sessionCertificate.Thumbprint, null);

        public void Dispose()
        {
            _listener.Stop();
            if (_ownsCertificates)
            {
                _sessionCertificate.Dispose();
                MasterCertificate.Dispose();
            }
        }
    }

    // Answers every question on a loopback port with the same reply, and
    // keeps track of the GetWakeAddress and SetPowerSchedule ones.
    private sealed class FakeAgent : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly System.Security.Cryptography.X509Certificates.X509Certificate2 _certificate;
        private readonly List<long> _schedulesReceived = new();
        private readonly List<long> _blocksReceived = new();
        private readonly List<long> _lessonsReceived = new();
        private readonly bool _ownsCertificate;
        private int _questions;

        // port and certificate: to stand in for an Agent that comes back where it was.
        public FakeAgent(
            System.Security.Cryptography.X509Certificates.X509Certificate2 masterCertificate, Message reply, int port = 0,
            System.Security.Cryptography.X509Certificates.X509Certificate2? certificate = null)
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            _ownsCertificate = certificate is null;
            _certificate = certificate ?? SelfSignedCertificate.Create("agent-under-test");
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            var masters = new TrustStore(new[] { masterCertificate.Thumbprint });
            Task.Run(() =>
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
                            if (message.Type == MessageType.GetWakeAddress)
                            {
                                Interlocked.Increment(ref _questions);
                            }
                            else if (message.Type == MessageType.SetAppBlock
                                     && AppBlockPolicy.TryDecode(message.Payload, out var block))
                            {
                                lock (_blocksReceived)
                                {
                                    _blocksReceived.Add(block.SavedAt);
                                }
                            }
                            else if (message.Type == MessageType.SetPowerSchedule
                                     && PowerSchedule.TryDecode(message.Payload, out var schedule))
                            {
                                lock (_schedulesReceived)
                                {
                                    _schedulesReceived.Add(schedule.SavedAt);
                                }
                            }
                            else if (message.Type == MessageType.SetClassLesson
                                     && ClassLesson.TryDecode(message.Payload, out var lesson))
                            {
                                lock (_lessonsReceived)
                                {
                                    _lessonsReceived.Add(lesson.SavedAt);
                                }
                            }
                            channel.Send(message.Type == MessageType.GetAppBlock && StatusReply is { } status ? status : reply);
                        }
                        catch (Exception)
                        {
                            // The poller gave up on this connection.
                        }
                    }
                }
            });
        }

        public int Port { get; }

        public string Thumbprint => _certificate.Thumbprint;

        // What GetAppBlock is answered with, when it isn't the same as everything else.
        public Message? StatusReply { get; set; }

        public int Questions => Volatile.Read(ref _questions);

        // The SavedAt of each SetAppBlock received, in order.
        public long[] BlocksReceived
        {
            get
            {
                lock (_blocksReceived)
                {
                    return _blocksReceived.ToArray();
                }
            }
        }

        // The SavedAt of each SetPowerSchedule received, in order.
        public long[] SchedulesReceived
        {
            get
            {
                lock (_schedulesReceived)
                {
                    return _schedulesReceived.ToArray();
                }
            }
        }

        // The SavedAt of each SetClassLesson received, in order.
        public long[] LessonsReceived
        {
            get
            {
                lock (_lessonsReceived)
                {
                    return _lessonsReceived.ToArray();
                }
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            if (_ownsCertificate)
            {
                _certificate.Dispose();
            }
        }
    }

    private sealed class FixedSizeCapture : IScreenCapture
    {
        public Bitmap CaptureFrame() => new(1920, 1080);
    }

    private sealed class NoInput : IInputInjector
    {
        public void Inject(InputEvent inputEvent)
        {
        }
    }

    private sealed class NoDevices : IInputDeviceController
    {
        public void DisableInputDevices()
        {
        }

        public void EnableInputDevices()
        {
        }
    }
}
