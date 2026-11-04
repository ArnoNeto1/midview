using System.Net;
using System.Net.Sockets;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

// The HoldSwitchUserBlock connection hides "Switch user" while it is open and
// restores it when it drops, through the real AgentServer over TLS.
public sealed class SwitchUserBlockServerTests
{
    private sealed class FakePolicy : ISwitchUserPolicy
    {
        private readonly object _gate = new();
        private bool _blocked;
        public bool Blocked { get { lock (_gate) { return _blocked; } } }
        public void Block() { lock (_gate) { _blocked = true; } }
        public void Unblock() { lock (_gate) { _blocked = false; } }
    }

    [Fact]
    public async Task HoldSwitchUserBlock_HidesSwitchUser_UntilTheConnectionDrops()
    {
        using var agentCert = SelfSignedCertificate.Create("agent-under-test");
        using var masterCert = SelfSignedCertificate.Create("master-under-test");
        var siteBlock = new SiteBlockCommandHandler(new SiteBlockState(), "unused", "unused-hosts", new FakeFirewallController());
        var policy = new FakePolicy();
        var handler = new SwitchUserBlockHandler(policy);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = new AgentServer(
            listener, agentCert, new TrustStore(new[] { masterCert.Thumbprint }), siteBlock, switchUserBlock: handler);
        using var stop = new CancellationTokenSource();
        var running = Task.Run(() => server.Run(stop.Token));

        var tcpClient = new TcpClient();
        tcpClient.Connect(IPAddress.Loopback, port);
        var channel = SecureChannel.AuthenticateAsClient(
            tcpClient, "agent-under-test", masterCert, new TrustStore(new[] { agentCert.Thumbprint }));
        channel.Send(new Message(MessageType.HoldSwitchUserBlock, Array.Empty<byte>()));

        var reply = await Task.Run(() => channel.Receive()).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(MessageType.CommandAck, reply.Type);
        Assert.True(CommandResult.Decode(reply.Payload).Succeeded);
        await WaitUntil(() => policy.Blocked);

        tcpClient.Dispose(); // the lock ended, or the teacher's PC dropped
        await WaitUntil(() => !policy.Blocked);

        stop.Cancel();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.True(condition());
            }
            await Task.Delay(50);
        }
    }
}
