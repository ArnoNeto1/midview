using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Agent;

public sealed class AgentServer
{
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
        while (!cancellationToken.IsCancellationRequested)
        {
            var tcpClient = _listener.AcceptTcpClient();
            HandleClient(tcpClient);
        }
    }

    private void HandleClient(TcpClient tcpClient)
    {
        using var channel = SecureChannel.AuthenticateAsServer(tcpClient, _serverCertificate, _masterTrustStore);
        var message = channel.Receive();
        var success = _commandHandler.Handle(message.Type, message.Payload);
        channel.Send(new Message(MessageType.CommandAck, new[] { (byte)(success ? 1 : 0) }));
    }
}
