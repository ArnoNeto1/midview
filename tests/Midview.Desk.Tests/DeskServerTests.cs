using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Desk;
using Midview.Protocol;
using Midview.Session;
using Xunit;

// The Desk helper's screen-and-input server, tested on the normal desktop with
// a fake capture and a fake injector: everything the real helper does except
// the SetThreadDesktop that only the secure (UAC) desktop needs, which is the
// injected attachThreadToDesktop seam (a recorder here, the real call in
// Task 7). A non-zero fake desktop handle makes the follower's first poll
// report a change, so the worker attaches and makes its capture as it would on
// a real machine.
public class DeskServerTests
{
    [Fact]
    public async Task Streams_frames_to_a_trusted_master()
    {
        using var harness = new DeskHarness(new FakeScreenCapture(200, 100));
        using var master = harness.ConnectMaster();
        master.Channel.Send(new Message(MessageType.ScreenStream, Array.Empty<byte>()));

        var message = await Task.Run(() => master.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(MessageType.ScreenFrame, message.Type);
        var frame = ScreenFrame.Decode(message.Payload);
        Assert.NotNull(frame);
        Assert.Equal((200, 100), (frame.ScreenWidth, frame.ScreenHeight));
        // The worker attached its thread to the desktop the follower reported
        // before it captured: the seam Task 7 fills with SetThreadDesktop.
        Assert.Contains(new IntPtr(1), harness.AttachedTo);
    }

    [Fact]
    public async Task Injects_the_masters_input_events()
    {
        using var harness = new DeskHarness(new FakeScreenCapture(200, 100));
        using var master = harness.ConnectMaster();
        master.Channel.Send(new Message(MessageType.ScreenStream, Array.Empty<byte>()));
        master.Channel.Send(new Message(MessageType.StartControl, Array.Empty<byte>()));

        var sent = new InputEvent(InputEventKind.MouseLeftDown, 42, 99, 7);
        master.Channel.Send(new Message(MessageType.InputEvent, InputEventCodec.Encode(sent)));

        await WaitUntil(() => harness.Injector.InjectCount >= 1);
        Assert.Equal(sent, harness.Injector.Injected[0]);
    }

    // Input only reaches the machine while the Master is controlling: an event
    // before StartControl is dropped, as SessionServer's control loop drops it.
    [Fact]
    public async Task Input_before_StartControl_is_not_injected()
    {
        using var harness = new DeskHarness(new FakeScreenCapture(200, 100));
        using var master = harness.ConnectMaster();
        master.Channel.Send(new Message(MessageType.ScreenStream, Array.Empty<byte>()));
        master.Channel.Send(new Message(MessageType.InputEvent, InputEventCodec.Encode(
            new InputEvent(InputEventKind.MouseMove, 10, 10, 0))));

        // A frame means the server has read past the input event above.
        await Task.Run(() => master.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(200);

        Assert.Equal(0, harness.Injector.InjectCount);
    }

    [Fact]
    public async Task A_server_cert_that_isnt_the_agent_thumbprint_is_rejected()
    {
        using var harness = new DeskHarness(new FakeScreenCapture(200, 100));

        // A Master that pins some other thumbprint as the server it trusts
        // rejects the agent certificate during the handshake, so it never
        // reaches the stream.
        using var otherCert = SelfSignedCertificate.Create("not-the-agent");
        await Assert.ThrowsAnyAsync<Exception>(() => Task.Run(() =>
        {
            using var tcpClient = new TcpClient();
            tcpClient.Connect(IPAddress.Loopback, harness.Port);
            using var channel = SecureChannel.AuthenticateAsClient(
                tcpClient, "desk-under-test", harness.MasterCert, new TrustStore(new[] { otherCert.Thumbprint }));
            channel.Send(new Message(MessageType.ScreenStream, Array.Empty<byte>()));
            channel.Send(new Message(MessageType.StartControl, Array.Empty<byte>()));
            channel.Send(new Message(MessageType.InputEvent, InputEventCodec.Encode(
                new InputEvent(InputEventKind.MouseLeftDown, 1, 2, 3))));
            channel.Receive();
        }).WaitAsync(TimeSpan.FromSeconds(10)));

        await Task.Delay(300);
        Assert.Equal(0, harness.Injector.InjectCount);
    }

    // One controlling Master at a time: while one trusted Master is served and
    // streaming, a second trusted Master is refused -- its connection is closed
    // before the worker starts, so its control input never reaches the injector.
    [Fact]
    public async Task A_second_concurrent_control_connection_is_refused()
    {
        using var harness = new DeskHarness(new FakeScreenCapture(200, 100));

        // The first Master is served and streaming, so it holds the slot.
        using var first = harness.ConnectMaster();
        first.Channel.Send(new Message(MessageType.ScreenStream, Array.Empty<byte>()));
        await Task.Run(() => first.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(10));

        // The second Master's handshake still succeeds, but the helper has given
        // the one serving slot to the first and refuses this one before the
        // worker/streaming starts.
        using var second = harness.ConnectMaster();
        second.Channel.Send(new Message(MessageType.ScreenStream, Array.Empty<byte>()));
        second.Channel.Send(new Message(MessageType.StartControl, Array.Empty<byte>()));
        second.Channel.Send(new Message(MessageType.InputEvent, InputEventCodec.Encode(
            new InputEvent(InputEventKind.MouseLeftDown, 5, 6, 7))));

        // The refused connection is closed: its Receive gives up rather than
        // ever returning a frame.
        await Assert.ThrowsAnyAsync<Exception>(() =>
            Task.Run(() => second.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(10)));

        await Task.Delay(200);
        Assert.Equal(0, harness.Injector.InjectCount);
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

    // A fixed-size screen that is never a real desktop: a frame the diff and
    // JPEG encoder accept, so the streaming path runs without a real screen.
    private sealed class FakeScreenCapture : IScreenCapture
    {
        private readonly int _width;
        private readonly int _height;

        public FakeScreenCapture(int width, int height)
        {
            _width = width;
            _height = height;
        }

        public Bitmap CaptureFrame()
        {
            var bitmap = new Bitmap(_width, _height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.CornflowerBlue);
            return bitmap;
        }
    }

    private sealed class FakeInputInjector : IInputInjector
    {
        private readonly List<InputEvent> _injected = new();

        public IReadOnlyList<InputEvent> Injected
        {
            get
            {
                lock (_injected)
                {
                    return _injected.ToList();
                }
            }
        }

        public int InjectCount
        {
            get
            {
                lock (_injected)
                {
                    return _injected.Count;
                }
            }
        }

        public void Inject(InputEvent inputEvent)
        {
            lock (_injected)
            {
                _injected.Add(inputEvent);
            }
        }
    }

    private sealed class DeskHarness : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serverTask;
        private readonly X509Certificate2 _agentCert = SelfSignedCertificate.Create("desk-under-test");
        private readonly List<IntPtr> _attachedTo = new();

        public DeskHarness(IScreenCapture capture)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            // A constant, non-zero handle: the first poll reports a change (so
            // the worker attaches and makes its capture) and every later poll
            // reports none (so it keeps the same one), as a settled desktop does.
            var follower = new DesktopFollower(() => new IntPtr(1));
            var server = new DeskServer(
                _listener, _agentCert, new TrustStore(new[] { MasterCert.Thumbprint }),
                () => capture, Injector, follower,
                desktop =>
                {
                    lock (_attachedTo)
                    {
                        _attachedTo.Add(desktop);
                    }
                });
            _serverTask = Task.Run(() => server.Run(_stop.Token));
        }

        public int Port { get; }

        public X509Certificate2 MasterCert { get; } = SelfSignedCertificate.Create("master-under-test");

        public FakeInputInjector Injector { get; } = new();

        public IReadOnlyList<IntPtr> AttachedTo
        {
            get
            {
                lock (_attachedTo)
                {
                    return _attachedTo.ToList();
                }
            }
        }

        public ConnectedMaster ConnectMaster()
        {
            var tcpClient = new TcpClient();
            tcpClient.Connect(IPAddress.Loopback, Port);
            var channel = SecureChannel.AuthenticateAsClient(
                tcpClient, "desk-under-test", MasterCert, new TrustStore(new[] { _agentCert.Thumbprint }));
            return new ConnectedMaster(tcpClient, channel);
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            try
            {
                _serverTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
            }
            _agentCert.Dispose();
            MasterCert.Dispose();
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

        public void Dispose()
        {
            Channel.Dispose();
            _tcpClient.Dispose();
        }
    }
}
