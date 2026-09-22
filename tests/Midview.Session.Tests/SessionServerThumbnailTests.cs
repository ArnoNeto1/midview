using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;
using Midview.Session;
using Xunit;

// A real SessionServer on a loopback port, plus any number of trusted
// clients connected to it -- the classroom grid's preview connection and a
// full-control connection are just two such clients.
public sealed class LoopbackSessionServer : IDisposable
{
    private readonly X509Certificate2 _serverCertificate = SelfSignedCertificate.Create("session-under-test");
    private readonly X509Certificate2 _clientCertificate = SelfSignedCertificate.Create("master-under-test");
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly List<SecureChannel> _channels = new();
    private readonly Task _serverTask;

    public LoopbackSessionServer(IScreenCapture screenCapture, FakeInputDeviceController inputDeviceController)
    {
        _listener.Start();
        var server = new SessionServer(
            _listener, _serverCertificate, new TrustStore(new[] { _clientCertificate.Thumbprint }),
            screenCapture, new FakeInputInjector(),
            new InputLockManager(inputDeviceController, new FakeClock(), TimeSpan.FromSeconds(5)));
        _serverTask = Task.Run(() => server.Run(CancellationToken.None));
    }

    public SecureChannel Connect()
    {
        var tcpClient = new TcpClient();
        tcpClient.Connect(IPAddress.Loopback, ((IPEndPoint)_listener.LocalEndpoint).Port);
        // Bounded, so a server that never answers (or never even finishes the
        // TLS handshake) fails the test instead of hanging it.
        tcpClient.ReceiveTimeout = 5000;
        var channel = SecureChannel.AuthenticateAsClient(
            tcpClient, "session-under-test", _clientCertificate, new TrustStore(new[] { _serverCertificate.Thumbprint }));
        _channels.Add(channel);
        return channel;
    }

    public void Dispose()
    {
        foreach (var channel in _channels)
        {
            channel.Dispose();
        }

        _listener.Stop(); // unblocks Run()'s AcceptTcpClient() with an expected exception
        try
        {
            _serverTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Expected: that exception is this helper's shutdown mechanism,
            // same as in SessionServerTests.
        }
    }
}

public class SessionServerThumbnailTests
{
    [Fact]
    public void RequestThumbnail_WithoutControl_RepliesWithThumbnailAndLeavesInputUnlocked()
    {
        var inputDeviceController = new FakeInputDeviceController();
        using var server = new LoopbackSessionServer(new CountingScreenCapture(), inputDeviceController);
        var preview = server.Connect();

        preview.Send(new Message(MessageType.RequestThumbnail, Array.Empty<byte>()));
        var reply = preview.Receive();

        // Not FrameTile: a preview connection must never get the full-control stream.
        Assert.Equal(MessageType.ThumbnailFrame, reply.Type);
        using var thumbnail = TileCodec.DecodeTile(reply.Payload);
        Assert.InRange(thumbnail.Width, 1, 320);
        Assert.InRange(thumbnail.Height, 1, 180);
        Assert.Equal(0, inputDeviceController.DisableCalls);
    }

    [Fact]
    public void RequestThumbnail_WhileAnotherConnectionIsControlling_StillReplies()
    {
        using var server = new LoopbackSessionServer(new CountingScreenCapture(), new FakeInputDeviceController());
        var control = server.Connect();
        control.Send(new Message(MessageType.StartControl, Array.Empty<byte>()));
        Assert.Equal(MessageType.FrameTile, control.Receive().Type); // full control is live

        var preview = server.Connect();
        preview.Send(new Message(MessageType.RequestThumbnail, Array.Empty<byte>()));

        Assert.Equal(MessageType.ThumbnailFrame, preview.Receive().Type);
    }

    [Fact]
    public void ClosingAPreviewConnection_DoesNotReleaseAnotherConnectionsInputLock()
    {
        var inputDeviceController = new FakeInputDeviceController();
        using var server = new LoopbackSessionServer(new CountingScreenCapture(), inputDeviceController);
        var control = server.Connect();
        control.Send(new Message(MessageType.StartControl, Array.Empty<byte>()));
        control.Receive(); // first FrameTile: StartControl has been handled, input is locked

        var preview = server.Connect();
        preview.Send(new Message(MessageType.RequestThumbnail, Array.Empty<byte>()));
        preview.Receive();
        preview.Dispose();
        Thread.Sleep(300); // let the server notice the closed preview connection and run its cleanup

        Assert.Equal(1, inputDeviceController.DisableCalls);
        Assert.Equal(0, inputDeviceController.EnableCalls);
    }
}
