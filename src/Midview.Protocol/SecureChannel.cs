using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Midview.Protocol;

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
        var sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        sslStream.AuthenticateAsServer(new SslServerAuthenticationOptions
        {
            ServerCertificate = serverCertificate,
            ClientCertificateRequired = true,
            CertificateChainPolicy = OfflineChainPolicy(),
            RemoteCertificateValidationCallback = (_, cert, _, _) => cert is not null && clientTrustStore.IsTrusted(cert),
        });

        return new SecureChannel(sslStream);
    }

    public static SecureChannel AuthenticateAsClient(
        TcpClient client, string targetHost, X509Certificate2 clientCertificate, TrustStore serverTrustStore)
    {
        var sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        sslStream.AuthenticateAsClient(new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            ClientCertificates = new X509CertificateCollection { clientCertificate },
            EnabledSslProtocols = SslProtocols.None,
            CertificateChainPolicy = OfflineChainPolicy(),
            RemoteCertificateValidationCallback = (_, cert, _, _) => cert is not null && serverTrustStore.IsTrusted(cert),
        });

        return new SecureChannel(sslStream);
    }

    public void Send(Message message) => MessageFramer.Write(_sslStream, message);

    public Message Receive() => MessageFramer.Read(_sslStream);

    public void Dispose() => _sslStream.Dispose();

    // Trust is the pinned thumbprint (TrustStore), never the certificate
    // chain, so the chain SslStream builds anyway must not reach the network:
    // on Windows, building one for a self-signed certificate can go looking
    // for roots and revocation lists online, which on a lab network without
    // internet (or with Midview's own internet block on) stalls every
    // handshake until that times out.
    private static X509ChainPolicy OfflineChainPolicy() => new()
    {
        RevocationMode = X509RevocationMode.NoCheck,
        DisableCertificateDownloads = true,
    };
}
