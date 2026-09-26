using System.Net;
using System.Net.Sockets;
using Midview.Agent;
using Midview.Protocol;
using Xunit;
using static Midview.Agent.WakeAddressLookup;

public class WakeAddressTests
{
    private static readonly byte[] LabCardMac = { 0x00, 0x1A, 0x2B, 0x3C, 0x4D, 0x5E };
    private static readonly byte[] WifiMac = { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 };

    private static readonly NetworkCard[] Cards =
    {
        new(new[] { IPAddress.Parse("10.0.0.7") }, WifiMac),
        new(new[] { IPAddress.Parse("fe80::1"), IPAddress.Parse("192.168.0.21") }, LabCardMac),
        new(new[] { IPAddress.Loopback }, Array.Empty<byte>()),
    };

    [Fact]
    public void Find_IsTheCardHoldingTheConnectionsAddress() =>
        Assert.Equal(LabCardMac, Find(IPAddress.Parse("192.168.0.21"), Cards));

    [Fact]
    public void Find_AnIPv4AddressSeenThroughIPv6_IsStillFound() =>
        Assert.Equal(LabCardMac, Find(IPAddress.Parse("192.168.0.21").MapToIPv6(), Cards));

    [Fact]
    public void Find_NoCardWithThatAddress_IsNull() =>
        Assert.Null(Find(IPAddress.Parse("172.16.0.9"), Cards));

    [Fact]
    public void Find_ACardWithoutAUsableAddress_IsNull() =>
        Assert.Null(Find(IPAddress.Loopback, Cards));

    [Fact]
    public async Task GetWakeAddress_ThroughTheAgentServer_AnswersWithTheCardOfTheConnection()
    {
        using var agentCert = SelfSignedCertificate.Create("agent-under-test");
        using var masterCert = SelfSignedCertificate.Create("master-under-test");
        var siteBlock = new SiteBlockCommandHandler(new SiteBlockState(), Path.GetTempFileName(), "unused-hosts-path", new FakeFirewallController());
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        IPAddress? askedAbout = null;
        var server = new AgentServer(
            listener, agentCert, new TrustStore(new[] { masterCert.Thumbprint }), siteBlock,
            findWakeAddress: address =>
            {
                askedAbout = address;
                return LabCardMac;
            });
        using var stopServer = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.Run(stopServer.Token));
        try
        {
            var reply = await Task.Run(() =>
            {
                using var tcpClient = new TcpClient();
                tcpClient.Connect(IPAddress.Loopback, port);
                using var channel = SecureChannel.AuthenticateAsClient(
                    tcpClient, "agent-under-test", masterCert, new TrustStore(new[] { agentCert.Thumbprint }));
                channel.Send(new Message(MessageType.GetWakeAddress, Array.Empty<byte>()));
                return channel.Receive();
            }).WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(MessageType.WakeAddress, reply.Type);
            Assert.Equal(LabCardMac, reply.Payload);
            Assert.Equal(IPAddress.Loopback, askedAbout);
        }
        finally
        {
            stopServer.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
