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
    private readonly PowerCommandHandler? _powerHandler;

    public AgentServer(
        TcpListener listener, X509Certificate2 serverCertificate, TrustStore masterTrustStore,
        SiteBlockCommandHandler commandHandler, PowerCommandHandler? powerHandler = null)
    {
        _listener = listener;
        _serverCertificate = serverCertificate;
        _masterTrustStore = masterTrustStore;
        _commandHandler = commandHandler;
        _powerHandler = powerHandler;
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

        // Power commands say what happened (a CommandResult, whose first
        // byte is the same 1/0 the site-blocking commands answer with).
        if (message.Type == MessageType.Power)
        {
            var (succeeded, detail) = _powerHandler?.Handle(message.Payload)
                ?? (false, "esta máquina não aceita desligamento");
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(succeeded, detail)));
            return;
        }

        var success = _commandHandler.Handle(message.Type, message.Payload);
        channel.Send(new Message(MessageType.CommandAck, new[] { (byte)(success ? 1 : 0) }));
    }
}
