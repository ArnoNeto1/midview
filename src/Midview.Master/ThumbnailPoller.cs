using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// Keeps one preview connection open to one machine and asks it for a
// thumbnail every few seconds, on a thread of its own: one slow or offline
// machine never delays any other machine's thumbnail. The connection stays
// open between polls -- a full TLS handshake every few seconds, times 25+
// machines, would be needless overhead.
public sealed class ThumbnailPoller : IDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    // Not much shorter than this: Windows retries a refused connection for
    // ~2 s before reporting it, and "refused" is what tells "nobody logged
    // in" apart from "offline".
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private const int ReplyTimeoutMs = 10_000;

    private readonly KnownMachine _machine;
    private readonly X509Certificate2 _masterCertificate;
    private readonly int _port;
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;

    public ThumbnailPoller(KnownMachine machine, X509Certificate2 masterCertificate, int port = ConnectionClient.SessionPort)
    {
        _machine = machine;
        _masterCertificate = masterCertificate;
        _port = port;
        _thread = new Thread(Run) { IsBackground = true, Name = $"Thumbnails: {machine.Name}" };
    }

    // Both raised on the poller's own thread. An empty thumbnail means the
    // machine answered but its screen can't be captured right now (e.g. it's
    // locked); the string is a short reason to show on the tile.
    public event Action<byte[]>? ThumbnailReceived;
    public event Action<string>? Unreachable;

    public void Start() => _thread.Start();

    // The thread notices within one poll interval or network timeout; it's a
    // background thread, so it never holds up Master from exiting.
    public void Dispose() => _stop.Cancel();

    private void Run()
    {
        TcpClient? tcpClient = null;
        SecureChannel? channel = null;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                if (channel is null)
                {
                    tcpClient = ConnectTcp();
                    channel = SecureChannel.AuthenticateAsClient(
                        tcpClient, _machine.HostAddress, _masterCertificate,
                        new TrustStore(new[] { _machine.SessionThumbprint }));
                }

                channel.Send(new Message(MessageType.RequestThumbnail, Array.Empty<byte>()));
                var reply = channel.Receive();
                if (reply.Type != MessageType.ThumbnailFrame)
                {
                    throw new InvalidDataException($"Expected a thumbnail, got {reply.Type}");
                }

                if (!_stop.IsCancellationRequested)
                {
                    ThumbnailReceived?.Invoke(reply.Payload);
                }
            }
            catch (Exception ex)
            {
                // Always start over with a fresh connection: a reply that
                // timed out could still arrive later and would then be read
                // as the answer to the next request.
                channel?.Dispose();
                tcpClient?.Dispose();
                channel = null;
                tcpClient = null;

                if (!_stop.IsCancellationRequested)
                {
                    Unreachable?.Invoke(FailureText.Status(ex));
                }
            }

            _stop.Token.WaitHandle.WaitOne(PollInterval);
        }

        channel?.Dispose();
        tcpClient?.Dispose();
    }

    private TcpClient ConnectTcp()
    {
        var tcpClient = TcpConnect.WithTimeout(_machine.HostAddress, _port, ConnectTimeout, _stop.Token);
        tcpClient.NoDelay = true;
        tcpClient.ReceiveTimeout = ReplyTimeoutMs;
        tcpClient.SendTimeout = ReplyTimeoutMs;
        return tcpClient;
    }
}
