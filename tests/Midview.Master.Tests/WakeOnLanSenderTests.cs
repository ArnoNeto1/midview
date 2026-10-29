using System.Net;
using System.Net.Sockets;
using Midview.Master;
using Midview.Protocol;
using Xunit;

public class WakeOnLanSenderTests
{
    [Fact]
    public void BroadcastTargets_AreEveryLocalNetworksBroadcast_PlusTheGeneralOne()
    {
        var targets = WakeOnLanSender.BroadcastTargets(new[]
        {
            (IPAddress.Parse("192.168.0.21"), IPAddress.Parse("255.255.255.0")),
            (IPAddress.Parse("10.1.2.3"), IPAddress.Parse("255.255.0.0")),
            (IPAddress.Parse("192.168.0.22"), IPAddress.Parse("255.255.255.0")), // same network again
            (IPAddress.Parse("172.16.5.5"), IPAddress.Parse("255.255.255.255")), // a /32: nobody else on it
            (IPAddress.Loopback, IPAddress.Parse("255.0.0.0")),
        });

        Assert.Equal(
            new[] { "255.255.255.255:9", "192.168.0.255:9", "10.1.255.255:9" },
            targets.Select(target => target.ToString()));
    }

    [Fact]
    public void DiscoveryTargets_IncludeBroadcastsAndUnicastSubnetHosts_SkippingSelf()
    {
        var targets = WakeOnLanSender.DiscoveryTargets(new[]
        {
            (IPAddress.Parse("10.103.217.105"), IPAddress.Parse("255.255.255.0")),
        }, Discovery.Port);

        Assert.Equal("255.255.255.255:28901", targets[0].ToString());
        Assert.Equal("10.103.217.255:28901", targets[1].ToString());
        Assert.Contains(new IPEndPoint(IPAddress.Parse("10.103.217.1"), Discovery.Port), targets);
        Assert.Contains(new IPEndPoint(IPAddress.Parse("10.103.217.215"), Discovery.Port), targets);
        Assert.Contains(new IPEndPoint(IPAddress.Parse("10.103.217.254"), Discovery.Port), targets);
        Assert.DoesNotContain(new IPEndPoint(IPAddress.Parse("10.103.217.105"), Discovery.Port), targets);
        Assert.Equal(2 + 253, targets.Count);
    }

    [Fact]
    public async Task Send_EveryMachinesMagicPacketReachesTheTarget()
    {
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var target = (IPEndPoint)receiver.Client.LocalEndPoint!;
        var first = new byte[] { 0x00, 0x1A, 0x2B, 0x3C, 0x4D, 0x5E };
        var second = new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 };

        var (sent, lastError) = WakeOnLanSender.Send(new[] { first, second }, new[] { target });

        Assert.Equal(6, sent); // 2 machines, 3 times each
        Assert.Null(lastError);
        var received = new List<byte[]>();
        for (var i = 0; i < 6; i++)
        {
            received.Add((await receiver.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Buffer);
        }
        Assert.Equal(3, received.Count(packet => packet.SequenceEqual(WakeOnLan.MagicPacket(first))));
        Assert.Equal(3, received.Count(packet => packet.SequenceEqual(WakeOnLan.MagicPacket(second))));
    }
}
