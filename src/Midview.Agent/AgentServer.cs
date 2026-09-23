using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Agent;

public sealed class AgentServer
{
    // Generous for a LAN round trip, but short enough that a connection which
    // never finishes its TLS handshake is dropped quickly.
    private const int TimeoutMs = 10_000;

    private readonly TcpListener _listener;
    private readonly X509Certificate2 _serverCertificate;
    private readonly TrustStore _masterTrustStore;
    private readonly SiteBlockCommandHandler _commandHandler;

    public AgentServer(
        TcpListener listener, X509Certificate2 serverCertificate, TrustStore masterTrustStore,
        SiteBlockCommandHandler commandHandler)
    {
        _listener = listener;
        _serverCertificate = serverCertificate;
        _masterTrustStore = masterTrustStore;
        _commandHandler = commandHandler;
    }

    public void Run(CancellationToken cancellationToken)
    {
        _listener.Start();
        using var registration = cancellationToken.Register(() => _listener.Stop());

        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient tcpClient;
            try
            {
                tcpClient = _listener.AcceptTcpClient();
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // Each connection on its own task: anything on the LAN can open a
            // TCP connection and then send nothing, and handled inline that
            // blocked every command -- the teacher's unblock included.
            _ = Task.Run(() => HandleClientSafely(tcpClient));
        }
    }

    private void HandleClientSafely(TcpClient tcpClient)
    {
        try
        {
            HandleClient(tcpClient);
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Error handling a client connection: {ex.Message}");
        }
    }

    private void HandleClient(TcpClient tcpClient)
    {
        using var client = tcpClient;
        client.ReceiveTimeout = TimeoutMs;
        client.SendTimeout = TimeoutMs;
        using var channel = SecureChannel.AuthenticateAsServer(tcpClient, _serverCertificate, _masterTrustStore);
        var message = channel.Receive();
        var success = _commandHandler.Handle(message.Type, message.Payload);
        channel.Send(new Message(MessageType.CommandAck, new[] { (byte)(success ? 1 : 0) }));
    }
}
