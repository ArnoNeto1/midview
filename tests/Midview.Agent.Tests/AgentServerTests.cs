using System.Net;
using System.Net.Sockets;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

public class AgentServerTests
{
    [Fact]
    public async Task ConnectionThatNeverHandshakes_DoesNotBlockTheNextCommand()
    {
        using var agentCert = SelfSignedCertificate.Create("agent-under-test");
        using var masterCert = SelfSignedCertificate.Create("master-under-test");
        var statePath = Path.Combine(Path.GetTempPath(), $"midview-state-{Guid.NewGuid()}.json");
        var firewall = new FakeFirewallController();
        var handler = new SiteBlockCommandHandler(new SiteBlockState(), statePath, "unused-hosts-path", firewall);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = new AgentServer(listener, agentCert, new TrustStore(new[] { masterCert.Thumbprint }), handler);

        Message SendCommand(MessageType type)
        {
            using var tcpClient = new TcpClient();
            tcpClient.Connect(IPAddress.Loopback, port);
            using var channel = SecureChannel.AuthenticateAsClient(
                tcpClient, "agent-under-test", masterCert, new TrustStore(new[] { agentCert.Thumbprint }));
            channel.Send(new Message(type, Array.Empty<byte>()));
            return channel.Receive();
        }

        using var stopServer = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.Run(stopServer.Token));
        try
        {
            // The process's first TLS handshake pays for loading SChannel and
            // the keys (seconds on a busy CI runner), which the 5 s below is
            // not about -- so one command goes through before the stall.
            var warmUp = await Task.Run(() => SendCommand(MessageType.UnblockInternet)).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(MessageType.CommandAck, warmUp.Type);

            // Opens a TCP connection and then sends nothing at all.
            using var stalledClient = new TcpClient();
            stalledClient.Connect(IPAddress.Loopback, port);

            // Well under the server's own 10 s handshake timeout, so this can
            // only pass if the stalled connection isn't in the way at all.
            var ack = await Task.Run(() => SendCommand(MessageType.BlockInternet)).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(MessageType.CommandAck, ack.Type);
            Assert.Equal(new byte[] { 1 }, ack.Payload);
            Assert.Equal(1, firewall.ApplyBlockCalls);
        }
        finally
        {
            stopServer.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
            File.Delete(statePath);
        }
    }
}
