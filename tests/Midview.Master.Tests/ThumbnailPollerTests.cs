using System.Drawing;
using System.Net;
using System.Net.Sockets;
using Midview.Master;
using Midview.Protocol;
using Midview.Session;
using Xunit;

public class ThumbnailPollerTests
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
            DeliveryFolder? deliveryFolder = null)
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            _sessionCertificate = sessionCertificate;
            MasterCertificate = masterCertificate;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            var server = new SessionServer(
                _listener, _sessionCertificate, new TrustStore(new[] { MasterCertificate.Thumbprint }),
                new FixedSizeCapture(), new NoInput(),
                new InputLockManager(new NoDevices(), new SystemClock(), TimeSpan.FromSeconds(5)),
                messageDisplay, chatWindows, fileInbox, appCloser, deliveryFolder);
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
