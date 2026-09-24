using System.Drawing;
using System.Net;
using System.Net.Sockets;
using Midview.Protocol;
using Midview.Session;
using Xunit;

public class CountingScreenCapture : IScreenCapture
{
    private int _colorSeed;
    public int CallCount { get; private set; }

    public Bitmap CaptureFrame()
    {
        CallCount++;
        _colorSeed = (_colorSeed + 1) % 255;
        var bitmap = new Bitmap(64, 64);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(_colorSeed, _colorSeed, _colorSeed)); // always different from the previous frame
        return bitmap;
    }
}

public class FakeInputInjector : IInputInjector
{
    public int InjectCount { get; private set; }
    public void Inject(InputEvent inputEvent) => InjectCount++;
}

// Throws like GdiScreenCapture does while the secure desktop (Win+L, a UAC
// prompt, Ctrl+Alt+Del) is showing, then recovers.
public class FailingThenWorkingScreenCapture : IScreenCapture
{
    private int _failuresLeft;

    public FailingThenWorkingScreenCapture(int failures)
    {
        _failuresLeft = failures;
    }

    public Bitmap CaptureFrame()
    {
        if (_failuresLeft-- > 0)
        {
            throw new System.ComponentModel.Win32Exception("The handle is invalid");
        }
        return new Bitmap(64, 64);
    }
}

public class FixedSizeScreenCapture : IScreenCapture
{
    private readonly int _width;
    private readonly int _height;

    public FixedSizeScreenCapture(int width, int height)
    {
        _width = width;
        _height = height;
    }

    public Bitmap CaptureFrame() => new(_width, _height);
}

public class RecordingMessageDisplay : IMessageDisplay
{
    private readonly List<string> _shown = new();

    public IReadOnlyList<string> Shown
    {
        get
        {
            lock (_shown)
            {
                return _shown.ToList();
            }
        }
    }

    public void Show(string text)
    {
        lock (_shown)
        {
            _shown.Add(text);
        }
    }
}

// Records what the Session asked the chat window to do, and lets a test
// type a line "as the student" through the callback it was given.
public class FakeChatWindows : IChatWindows
{
    private readonly List<FakeChatWindow> _opened = new();

    public IReadOnlyList<FakeChatWindow> Opened
    {
        get
        {
            lock (_opened)
            {
                return _opened.ToList();
            }
        }
    }

    public IChatWindow Open(Func<string, bool> sendToTeacher)
    {
        var window = new FakeChatWindow(sendToTeacher);
        lock (_opened)
        {
            _opened.Add(window);
        }
        return window;
    }
}

public class FakeChatWindow : IChatWindow
{
    private readonly List<string> _teacherLines = new();

    public FakeChatWindow(Func<string, bool> sendToTeacher)
    {
        StudentTypes = sendToTeacher;
    }

    public Func<string, bool> StudentTypes { get; }

    public bool Ended { get; private set; }

    public IReadOnlyList<string> TeacherLines
    {
        get
        {
            lock (_teacherLines)
            {
                return _teacherLines.ToList();
            }
        }
    }

    public void ShowTeacherMessage(string text)
    {
        lock (_teacherLines)
        {
            _teacherLines.Add(text);
        }
    }

    public void ShowConversationEnded() => Ended = true;
}

public class SessionServerTests
{
    [Fact]
    public void CaptureCadence_IsDecoupledFromMessageRate()
    {
        var capture = new CountingScreenCapture();
        var injector = new FakeInputInjector();
        var serverCert = SelfSignedCertificate.Create("session-under-test");
        var clientCert = SelfSignedCertificate.Create("master-under-test");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var server = new SessionServer(
            listener, serverCert, new TrustStore(new[] { clientCert.Thumbprint }),
            capture, injector,
            new InputLockManager(new FakeInputDeviceController(), new FakeClock(), TimeSpan.FromSeconds(5)));

        using var stopServer = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.Run(stopServer.Token));

        using var tcpClient = new TcpClient();
        tcpClient.Connect(IPAddress.Loopback, port);
        using var clientChannel = SecureChannel.AuthenticateAsClient(
            tcpClient, "session-under-test", clientCert, new TrustStore(new[] { serverCert.Thumbprint }));

        clientChannel.Send(new Message(MessageType.StartControl, Array.Empty<byte>()));

        // Flood far more input events than any capture pipeline should ever
        // process 1:1 -- if capture were still tied to message arrival, this
        // alone would drive thousands of captures in well under a second.
        var inputPayload = new byte[11];
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(1))
        {
            clientChannel.Send(new Message(MessageType.InputEvent, inputPayload));
        }

        clientChannel.Send(new Message(MessageType.StopControl, Array.Empty<byte>()));
        tcpClient.Client.Shutdown(SocketShutdown.Send);
        Thread.Sleep(300); // let the capture thread's in-flight interval finish
        stopServer.Cancel();
        listener.Stop(); // unblocks the now-idle AcceptTcpClient() with an expected shutdown exception
        try
        {
            serverTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Expected: stopping the listener while Run()'s loop is blocked
            // in AcceptTcpClient() makes it throw -- that's this test's own
            // shutdown mechanism, not a defect under test.
        }

        // ~1s at a 66ms capture interval is ~15 captures; assert well under
        // the thousands a message-coupled loop would have produced instead.
        Assert.InRange(capture.CallCount, 1, 60);
    }

    [Fact]
    public async Task CaptureFailures_AreRetried_AndFramesStillArrive()
    {
        using var harness = new ServerHarness(new FailingThenWorkingScreenCapture(failures: 3));
        using var master = harness.ConnectMaster();
        master.StartControl();

        var firstMessage = await Task.Run(() => master.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(MessageType.FrameTile, firstMessage.Type);
    }

    [Fact]
    public async Task ConnectionThatNeverHandshakes_DoesNotBlockAMaster()
    {
        using var harness = new ServerHarness(new CountingScreenCapture());
        using var stalledClient = new TcpClient();
        stalledClient.Connect(IPAddress.Loopback, harness.Port); // then sends nothing at all

        // Well under the server's own 10 s handshake timeout.
        var firstMessage = await Task.Run(() =>
        {
            using var master = harness.ConnectMaster();
            master.StartControl();
            return master.Channel.Receive();
        }).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(MessageType.FrameTile, firstMessage.Type);
    }

    [Fact]
    public async Task RequestThumbnail_NoControlSession_RepliesWithTheScreenScaledDown()
    {
        using var harness = new ServerHarness(new FixedSizeScreenCapture(1920, 1080));
        using var preview = harness.ConnectMaster();

        var reply = await Task.Run(() => preview.RequestThumbnail()).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(MessageType.ThumbnailFrame, reply.Type);
        using var thumbnail = TileCodec.DecodeTile(reply.Payload);
        Assert.Equal(ThumbnailRenderer.MaxWidth, thumbnail.Width);
        Assert.Equal(ThumbnailRenderer.MaxHeight, thumbnail.Height);
    }

    [Fact]
    public async Task RequestThumbnail_WhileAnotherMasterIsControlling_StillReplies()
    {
        using var harness = new ServerHarness(new FixedSizeScreenCapture(640, 360));
        using var controller = harness.ConnectMaster();
        controller.StartControl();
        await Task.Run(() => controller.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(5)); // it holds the control slot now

        using var preview = harness.ConnectMaster();

        // Well under the 15 s a second *controlling* Master would wait for
        // the slot, so this only passes if previews don't need it.
        var reply = await Task.Run(() => preview.RequestThumbnail()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(MessageType.ThumbnailFrame, reply.Type);
        Assert.NotEmpty(reply.Payload);
    }

    [Fact]
    public async Task ControlConnection_WhilePreviewIsOpen_IsServed()
    {
        using var harness = new ServerHarness(new FixedSizeScreenCapture(640, 360));
        using var preview = harness.ConnectMaster();
        await Task.Run(() => preview.RequestThumbnail()).WaitAsync(TimeSpan.FromSeconds(5));

        using var controller = harness.ConnectMaster();
        controller.StartControl();

        var firstMessage = await Task.Run(() => controller.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(MessageType.FrameTile, firstMessage.Type);
    }

    [Fact]
    public async Task RequestThumbnail_CaptureFails_RepliesEmptyAndKeepsTheConnectionOpen()
    {
        using var harness = new ServerHarness(new FailingThenWorkingScreenCapture(failures: int.MaxValue));
        using var preview = harness.ConnectMaster();

        var first = await Task.Run(() => preview.RequestThumbnail()).WaitAsync(TimeSpan.FromSeconds(5));
        var second = await Task.Run(() => preview.RequestThumbnail()).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(MessageType.ThumbnailFrame, first.Type);
        Assert.Empty(first.Payload);
        Assert.Equal(MessageType.ThumbnailFrame, second.Type);
        Assert.Empty(second.Payload);
    }

    [Fact]
    public async Task ShowMessage_ShowsTheTextAndAcknowledges()
    {
        var display = new RecordingMessageDisplay();
        using var harness = new ServerHarness(new CountingScreenCapture(), display);
        using var sender = harness.ConnectMaster();

        var ack = await Task.Run(() => sender.ShowMessage("Atenção: prova em 5 minutos")).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(MessageType.CommandAck, ack.Type);
        Assert.Equal(new byte[] { 1 }, ack.Payload);
        Assert.Equal(new[] { "Atenção: prova em 5 minutos" }, display.Shown);
    }

    [Fact]
    public async Task ShowMessage_NothingToShow_RefusesWithoutShowingAnything()
    {
        var display = new RecordingMessageDisplay();
        using var harness = new ServerHarness(new CountingScreenCapture(), display);
        using var sender = harness.ConnectMaster();

        var ack = await Task.Run(() => sender.ShowMessage("   ")).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new byte[] { 0 }, ack.Payload);
        Assert.Empty(display.Shown);
    }

    [Fact]
    public async Task ShowMessage_WhileAnotherMasterIsControlling_IsStillShown()
    {
        var display = new RecordingMessageDisplay();
        using var harness = new ServerHarness(new CountingScreenCapture(), display);
        using var controller = harness.ConnectMaster();
        controller.StartControl();
        await Task.Run(() => controller.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(5)); // it holds the control slot now

        using var sender = harness.ConnectMaster();

        // Well under the 15 s a second controlling Master would wait for the slot.
        var ack = await Task.Run(() => sender.ShowMessage("Olá")).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new byte[] { 1 }, ack.Payload);
    }

    [Fact]
    public async Task Chat_OpensAWindowAndShowsTheTeachersLines_IgnoringHeartbeats()
    {
        var chats = new FakeChatWindows();
        using var harness = new ServerHarness(new CountingScreenCapture(), chatWindows: chats);
        using var teacher = harness.ConnectMaster();

        teacher.Channel.Send(new Message(MessageType.ChatOpen, Array.Empty<byte>()));
        teacher.Channel.Send(new Message(MessageType.Heartbeat, Array.Empty<byte>()));
        teacher.Channel.Send(new Message(MessageType.ChatText, TextMessage.Encode("Tudo certo aí?")));

        await WaitUntil(() => chats.Opened.Count == 1 && chats.Opened[0].TeacherLines.Count == 1);
        Assert.Equal(new[] { "Tudo certo aí?" }, chats.Opened[0].TeacherLines);
    }

    [Fact]
    public async Task Chat_StudentsLineReachesTheTeacher()
    {
        var chats = new FakeChatWindows();
        using var harness = new ServerHarness(new CountingScreenCapture(), chatWindows: chats);
        using var teacher = harness.ConnectMaster();
        teacher.Channel.Send(new Message(MessageType.ChatOpen, Array.Empty<byte>()));
        await WaitUntil(() => chats.Opened.Count == 1);

        Assert.True(chats.Opened[0].StudentTypes("Não consigo abrir o arquivo"));

        var line = await Task.Run(() => teacher.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(MessageType.ChatText, line.Type);
        Assert.Equal("Não consigo abrir o arquivo", TextMessage.Decode(line.Payload));
    }

    [Fact]
    public async Task Chat_TeacherLeaves_EndsTheConversationOnTheStudentsSide()
    {
        var chats = new FakeChatWindows();
        using var harness = new ServerHarness(new CountingScreenCapture(), chatWindows: chats);
        var teacher = harness.ConnectMaster();
        teacher.Channel.Send(new Message(MessageType.ChatOpen, Array.Empty<byte>()));
        await WaitUntil(() => chats.Opened.Count == 1);

        teacher.Dispose();

        await WaitUntil(() => chats.Opened[0].Ended);
        Assert.False(chats.Opened[0].StudentTypes("alguém aí?"));
    }

    [Fact]
    public async Task Chat_WhileAnotherMasterIsControlling_StillOpens()
    {
        var chats = new FakeChatWindows();
        using var harness = new ServerHarness(new CountingScreenCapture(), chatWindows: chats);
        using var controller = harness.ConnectMaster();
        controller.StartControl();
        await Task.Run(() => controller.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(5)); // it holds the control slot now

        using var teacher = harness.ConnectMaster();
        teacher.Channel.Send(new Message(MessageType.ChatOpen, Array.Empty<byte>()));

        await WaitUntil(() => chats.Opened.Count == 1);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the condition");
            await Task.Delay(20);
        }
    }

    private sealed class ServerHarness : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stopServer = new();
        private readonly Task _serverTask;
        private readonly System.Security.Cryptography.X509Certificates.X509Certificate2 _serverCert =
            SelfSignedCertificate.Create("session-under-test");
        private readonly System.Security.Cryptography.X509Certificates.X509Certificate2 _masterCert =
            SelfSignedCertificate.Create("master-under-test");

        public ServerHarness(
            IScreenCapture capture, IMessageDisplay? messageDisplay = null, IChatWindows? chatWindows = null)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            var server = new SessionServer(
                _listener, _serverCert, new TrustStore(new[] { _masterCert.Thumbprint }),
                capture, new FakeInputInjector(),
                new InputLockManager(new FakeInputDeviceController(), new FakeClock(), TimeSpan.FromSeconds(5)),
                messageDisplay, chatWindows);
            _serverTask = Task.Run(() => server.Run(_stopServer.Token));
        }

        public int Port { get; }

        public ConnectedMaster ConnectMaster()
        {
            var tcpClient = new TcpClient();
            tcpClient.Connect(IPAddress.Loopback, Port);
            var channel = SecureChannel.AuthenticateAsClient(
                tcpClient, "session-under-test", _masterCert, new TrustStore(new[] { _serverCert.Thumbprint }));
            return new ConnectedMaster(tcpClient, channel);
        }

        public void Dispose()
        {
            _stopServer.Cancel();
            _listener.Stop(); // unblocks AcceptTcpClient() with an expected exception, as in the test above
            try
            {
                _serverTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
            }
            _serverCert.Dispose();
            _masterCert.Dispose();
        }
    }

    private sealed class ConnectedMaster : IDisposable
    {
        private readonly TcpClient _tcpClient;

        public ConnectedMaster(TcpClient tcpClient, SecureChannel channel)
        {
            _tcpClient = tcpClient;
            Channel = channel;
        }

        public SecureChannel Channel { get; }

        // What ViewControlWindow sends as soon as it opens.
        public void StartControl() => Channel.Send(new Message(MessageType.StartControl, Array.Empty<byte>()));

        public Message ShowMessage(string text)
        {
            Channel.Send(new Message(MessageType.ShowMessage, TextMessage.Encode(text)));
            return Channel.Receive();
        }

        public Message RequestThumbnail()
        {
            Channel.Send(new Message(MessageType.RequestThumbnail, Array.Empty<byte>()));
            return Channel.Receive();
        }

        public void Dispose()
        {
            Channel.Dispose();
            _tcpClient.Dispose();
        }
    }
}
