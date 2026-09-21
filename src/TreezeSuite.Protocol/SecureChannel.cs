using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace TreezeSuite.Protocol;

public sealed class SecureChannel : IDisposable
{
    private readonly SslStream _sslStream;

    private SecureChannel(SslStream sslStream)
    {
        _sslStream = sslStream;
    }

    public static SecureChannel AuthenticateAsServer(
        TcpClient client, X509Certificate2 serverCertificate, TrustStore clientTrustStore)
    {
        var sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false,
            userCertificateValidationCallback: (_, cert, _, _) =>
                cert is not null && clientTrustStore.IsTrusted(cert));

        sslStream.AuthenticateAsServer(serverCertificate, clientCertificateRequired: true,
            checkCertificateRevocation: false);

        return new SecureChannel(sslStream);
    }

    public static SecureChannel AuthenticateAsClient(
        TcpClient client, string targetHost, X509Certificate2 clientCertificate, TrustStore serverTrustStore)
    {
        var sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false,
            userCertificateValidationCallback: (_, cert, _, _) =>
                cert is not null && serverTrustStore.IsTrusted(cert));

        var certificates = new X509CertificateCollection { clientCertificate };
        sslStream.AuthenticateAsClient(targetHost, certificates, SslProtocols.Tls13, checkCertificateRevocation: false);

        return new SecureChannel(sslStream);
    }

    public void Send(Message message) => MessageFramer.Write(_sslStream, message);

    public Message Receive() => MessageFramer.Read(_sslStream);

    public void Dispose() => _sslStream.Dispose();
}
