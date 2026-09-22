using System.Collections.Concurrent;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Media.Imaging;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// Stands in for a Session on a loopback port: accepts one connection and
// answers every RequestThumbnail with the same 64x36 JPEG.
public sealed class FakeThumbnailServer : IDisposable
{
    private readonly X509Certificate2 _certificate = SelfSignedCertificate.Create("fake-session");
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public FakeThumbnailServer(X509Certificate2 trustedMasterCertificate)
    {
        _listener.Start();
        byte[] jpegBytes;
        using (var image = new Bitmap(64, 36))
        {
            jpegBytes = TileCodec.EncodeTile(image, new Rectangle(0, 0, 64, 36));
        }

        Task.Run(() =>
        {
            try
            {
                using var tcpClient = _listener.AcceptTcpClient();
                using var channel = SecureChannel.AuthenticateAsServer(
                    tcpClient, _certificate, new TrustStore(new[] { trustedMasterCertificate.Thumbprint }));
                while (true)
                {
                    if (channel.Receive().Type == MessageType.RequestThumbnail)
                    {
                        channel.Send(new Message(MessageType.ThumbnailFrame, jpegBytes));
                    }
                }
            }
            catch (Exception)
            {
                // The poller disconnected or the test ended.
            }
        });
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public string Thumbprint => _certificate.Thumbprint;

    public void Dispose() => _listener.Stop();
}

public class MachinePreviewPollerTests
{
    [Fact]
    public void ReachableSession_ReportsItsThumbnail()
    {
        var masterCertificate = SelfSignedCertificate.Create("master-under-test");
        using var session = new FakeThumbnailServer(masterCertificate);
        var results = new BlockingCollection<BitmapSource?>();
        using var poller = new MachinePreviewPoller(
            new KnownMachine("Aluno-25", "127.0.0.1", session.Thumbprint, null), session.Port,
            masterCertificate, TimeSpan.FromMilliseconds(100), results.Add);

        poller.Start();

        Assert.True(results.TryTake(out var thumbnail, TimeSpan.FromSeconds(10)));
        Assert.NotNull(thumbnail);
        Assert.Equal(64, thumbnail.PixelWidth);
        Assert.Equal(36, thumbnail.PixelHeight);
        Assert.True(thumbnail.IsFrozen); // the UI thread can only use it if it's frozen
    }

    [Fact]
    public void NothingListening_ReportsNullInsteadOfThrowing()
    {
        var unusedListener = new TcpListener(IPAddress.Loopback, 0);
        unusedListener.Start();
        var closedPort = ((IPEndPoint)unusedListener.LocalEndpoint).Port;
        unusedListener.Stop();
        var results = new BlockingCollection<BitmapSource?>();
        using var poller = new MachinePreviewPoller(
            new KnownMachine("Aluno-26", "127.0.0.1", "0000", null), closedPort,
            SelfSignedCertificate.Create("master-under-test"), TimeSpan.FromMilliseconds(100), results.Add);

        poller.Start();

        Assert.True(results.TryTake(out var thumbnail, TimeSpan.FromSeconds(10)));
        Assert.Null(thumbnail);
    }
}
