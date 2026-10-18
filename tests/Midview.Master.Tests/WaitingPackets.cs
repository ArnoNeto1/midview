using System.Net;
using System.Net.Sockets;
using Xunit;

// Answers that arrived in time but are not read yet, as a busy PC leaves
// them in its socket.
public static class WaitingPackets
{
    // Sends the packets to udp over loopback and returns once they are all
    // waiting there. On Windows, Available counts every packet waiting, not
    // only the first.
    public static void Queue(UdpClient udp, params byte[][] packets)
    {
        using var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var to = new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)udp.Client.LocalEndPoint!).Port);
        foreach (var packet in packets)
        {
            sender.Send(packet, packet.Length, to);
        }
        var total = packets.Sum(packet => packet.Length);
        Assert.True(SpinWait.SpinUntil(() => udp.Available >= total, TimeSpan.FromSeconds(10)), "The packets never all arrived.");
    }
}
