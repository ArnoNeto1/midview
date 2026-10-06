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
        RemoteThumbprint = ThumbprintOf(sslStream.RemoteCertificate);
    }

    // The other end's certificate thumbprint, upper case ("" if it sent none).
    public string RemoteThumbprint { get; }

    public static SecureChannel AuthenticateAsServer(
        TcpClient client, X509Certificate2 serverCertificate, TrustStore clientTrustStore) =>
        Server(client, serverCertificate, clientTrustStore.IsTrusted);

    // For the Agent, which lets a teacher it doesn't trust yet through the
    // handshake to ask to pair (see Pairing). A certificate is still
    // required, so RemoteThumbprint always names one: what that teacher may
    // do is decided after the handshake, from it.
    public static SecureChannel AuthenticateAsServerAcceptingAnyClient(TcpClient client, X509Certificate2 serverCertificate) =>
        Server(client, serverCertificate, _ => true);

    public static SecureChannel AuthenticateAsClient(
        TcpClient client, string targetHost, X509Certificate2 clientCertificate, TrustStore serverTrustStore) =>
        Client(client, targetHost, clientCertificate, serverTrustStore.IsTrusted);

    // The teacher's side of pairing: the machine's certificate isn't known
    // yet, and the pairing code is what ties it to the machine on the desk.
    public static SecureChannel AuthenticateAsClientAcceptingAnyServer(
        TcpClient client, string targetHost, X509Certificate2 clientCertificate) =>
        Client(client, targetHost, clientCertificate, _ => true);

    public void Send(Message message) => MessageFramer.Write(_sslStream, message);

    public Message Receive() => MessageFramer.Read(_sslStream);

    public void Dispose() => _sslStream.Dispose();

    private static SecureChannel Server(TcpClient client, X509Certificate2 serverCertificate, Func<X509Certificate, bool> accept)
    {
        var sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        sslStream.AuthenticateAsServer(new SslServerAuthenticationOptions
        {
            ServerCertificate = serverCertificate,
            ClientCertificateRequired = true,
            CertificateChainPolicy = OfflineChainPolicy(),
            RemoteCertificateValidationCallback = (_, cert, _, _) => cert is not null && accept(cert),
        });

        return new SecureChannel(sslStream);
    }

    private static SecureChannel Client(
        TcpClient client, string targetHost, X509Certificate2 clientCertificate, Func<X509Certificate, bool> accept)
    {
        var sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        sslStream.AuthenticateAsClient(new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            ClientCertificates = new X509CertificateCollection { clientCertificate },
            EnabledSslProtocols = SslProtocols.None,
            CertificateChainPolicy = OfflineChainPolicy(),
            RemoteCertificateValidationCallback = (_, cert, _, _) => cert is not null && accept(cert),
        });

        return new SecureChannel(sslStream);
    }

    private static string ThumbprintOf(X509Certificate? certificate)
    {
        if (certificate is null)
        {
            return string.Empty;
        }

        using var certificate2 = new X509Certificate2(certificate);
        return certificate2.Thumbprint.ToUpperInvariant();
    }

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
