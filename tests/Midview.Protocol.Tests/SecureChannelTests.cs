using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using Midview.Protocol;
using Xunit;

public class SecureChannelTests
{
    [Fact]
    public void AuthenticateAsClientAndServer_TrustedCertificates_ExchangesMessage()
    {
        using var serverCert = SelfSignedCertificate.Create("session-under-test");
        using var clientCert = SelfSignedCertificate.Create("master-under-test");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Message? received = null;
        var serverTask = Task.Run(() =>
        {
            using var tcpClient = listener.AcceptTcpClient();
            using var channel = SecureChannel.AuthenticateAsServer(
                tcpClient, serverCert, new TrustStore(new[] { clientCert.Thumbprint }));
            channel.Send(new Message(MessageType.Hello, new byte[] { 42 }));
        });

        using var connectingClient = new TcpClient();
        connectingClient.Connect(IPAddress.Loopback, port);
        using var clientChannel = SecureChannel.AuthenticateAsClient(
            connectingClient, "session-under-test", clientCert,
            new TrustStore(new[] { serverCert.Thumbprint }));

        received = clientChannel.Receive();
        serverTask.Wait(TimeSpan.FromSeconds(5));

        listener.Stop();
        Assert.Equal(MessageType.Hello, received.Type);
        Assert.Equal(new byte[] { 42 }, received.Payload);
    }

    [Fact]
    public void AuthenticateAsServer_UntrustedClientCertificate_Throws()
    {
        using var serverCert = SelfSignedCertificate.Create("session-under-test");
        using var clientCert = SelfSignedCertificate.Create("untrusted-master");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(() =>
        {
            using var tcpClient = listener.AcceptTcpClient();
            Assert.Throws<AuthenticationException>(() =>
                SecureChannel.AuthenticateAsServer(tcpClient, serverCert, new TrustStore(Array.Empty<string>())));
        });

        using var connectingClient = new TcpClient();
        connectingClient.Connect(IPAddress.Loopback, port);
        // The server-side rejection is the property that actually matters here
        // (asserted above, inside serverTask). Whether .NET's SslStream also
        // surfaces that rejection synchronously on the client's own
        // AuthenticateAsClient call, versus only once the client subsequently
        // tries to use the (unusable) connection, is an SslStream
        // implementation detail -- so this covers both: either the handshake
        // itself throws, or a following Send/Receive does.
        Assert.ThrowsAny<Exception>(() =>
        {
            using var clientChannel = SecureChannel.AuthenticateAsClient(
                connectingClient, "session-under-test", clientCert,
                new TrustStore(new[] { serverCert.Thumbprint }));
            clientChannel.Send(new Message(MessageType.Hello, Array.Empty<byte>()));
            clientChannel.Receive();
        });

        serverTask.Wait(TimeSpan.FromSeconds(5));
        listener.Stop();
    }
}
