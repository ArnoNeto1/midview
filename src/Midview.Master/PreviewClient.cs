using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// The classroom grid's connection to one machine's Session. It stays open
// between polls (a fresh TLS handshake to every machine every few seconds
// would be needless overhead) and only ever asks for thumbnails: it never
// sends StartControl, so the Session never starts its capture thread or
// locks the student's input because of it.
public sealed class PreviewClient : IDisposable
{
    private readonly SecureChannel _channel;

    private PreviewClient(SecureChannel channel)
    {
        _channel = channel;
    }

    // Every step is bounded by the timeout, connecting included: a plain
    // TcpClient.Connect to a powered-off machine can hang for ~20 s.
    public static PreviewClient Connect(
        string hostAddress, int port, string targetThumbprint, X509Certificate2 masterCertificate, TimeSpan timeout)
    {
        var tcpClient = new TcpClient();
        try
        {
            if (!tcpClient.ConnectAsync(hostAddress, port).Wait(timeout))
            {
                throw new TimeoutException($"Connecting to {hostAddress}:{port} timed out");
            }

            tcpClient.NoDelay = true;
            tcpClient.ReceiveTimeout = (int)timeout.TotalMilliseconds;
            tcpClient.SendTimeout = (int)timeout.TotalMilliseconds;
            var channel = SecureChannel.AuthenticateAsClient(
                tcpClient, hostAddress, masterCertificate, new TrustStore(new[] { targetThumbprint }));
            return new PreviewClient(channel);
        }
        catch
        {
            tcpClient.Dispose();
            throw;
        }
    }

    // Returns the JPEG bytes of one downscaled screenshot.
    public byte[] RequestThumbnail()
    {
        _channel.Send(new Message(MessageType.RequestThumbnail, Array.Empty<byte>()));
        var reply = _channel.Receive();
        if (reply.Type != MessageType.ThumbnailFrame)
        {
            throw new InvalidDataException($"Expected {MessageType.ThumbnailFrame}, got {reply.Type}");
        }
        return reply.Payload;
    }

    public void Dispose() => _channel.Dispose();
}
