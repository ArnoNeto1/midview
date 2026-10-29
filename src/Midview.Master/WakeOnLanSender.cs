using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Midview.Protocol;

namespace Midview.Master;

// Sends the magic packets that turn machines on (see WakeOnLan). A machine
// that's off has no IP address to send to, so they're broadcast: to
// 255.255.255.255, which Windows sends out of just one network card, and to
// each of this PC's own networks' broadcast address (192.168.0.255 on
// 192.168.0.x/24), so they go out of every card. Each packet goes a few
// times: it's UDP, nothing confirms it arrived, and a machine turning on is
// the only answer.
public static class WakeOnLanSender
{
    private const int Repeats = 3;
    private static readonly TimeSpan RepeatGap = TimeSpan.FromMilliseconds(100);

    // Maximum subnet size scanned host-by-host (a /22 = 1022 hosts). Wider
    // masks (like /16) scan the teacher's own /24 block.
    private const uint MaxDirectScanHostMask = 0x0000_03FFu;
    private const uint Slash24Mask = 0xFFFF_FF00u;

    // Broadcast addresses for Wake-on-LAN (and the first targets of network discovery).
    public static IReadOnlyList<IPEndPoint> BroadcastTargets(
        IEnumerable<(IPAddress Address, IPAddress Mask)> networks, int port = WakeOnLan.Port)
    {
        var targets = new List<IPEndPoint> { new(IPAddress.Broadcast, port) };
        foreach (var (address, mask) in networks)
        {
            if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
            {
                continue;
            }

            var addressBytes = address.GetAddressBytes();
            var maskBytes = mask.GetAddressBytes();
            var broadcast = new byte[4];
            for (var i = 0; i < 4; i++)
            {
                broadcast[i] = (byte)(addressBytes[i] | ~maskBytes[i]);
            }

            // A /32 has no one else on it; its "broadcast" is the PC itself.
            var target = new IPEndPoint(new IPAddress(broadcast), port);
            if (!broadcast.AsSpan().SequenceEqual(addressBytes) && !targets.Contains(target))
            {
                targets.Add(target);
            }
        }
        return targets;
    }

    // Where "Procurar na rede" and "Entrar na sala de um colega" send their
    // UDP probes: the broadcast addresses first, followed by every unicast host
    // address on each local subnet (so discovery works even when a school Wi-Fi
    // access point drops broadcast traffic or Windows Firewall is on a Public
    // profile that blocks broadcast replies).
    public static IReadOnlyList<IPEndPoint> DiscoveryTargets(
        IEnumerable<(IPAddress Address, IPAddress Mask)> networks, int port = Discovery.Port)
    {
        var networkList = networks.ToList();
        var targets = new List<IPEndPoint>(BroadcastTargets(networkList, port));
        var seen = new HashSet<IPEndPoint>(targets);
        Span<byte> hostBytes = stackalloc byte[4];

        foreach (var (address, mask) in networkList)
        {
            if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
            {
                continue;
            }

            var ip = BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());
            var netmask = BinaryPrimitives.ReadUInt32BigEndian(mask.GetAddressBytes());
            var hostMask = ~netmask;
            if (hostMask <= 1)
            {
                continue;
            }

            if (hostMask > MaxDirectScanHostMask)
            {
                netmask = Slash24Mask;
                hostMask = ~netmask;
            }

            var network = ip & netmask;
            var broadcast = network | hostMask;
            for (var host = network + 1; host < broadcast; host++)
            {
                if (host == ip)
                {
                    continue;
                }

                BinaryPrimitives.WriteUInt32BigEndian(hostBytes, host);
                var endpoint = new IPEndPoint(new IPAddress(hostBytes), port);
                if (seen.Add(endpoint))
                {
                    targets.Add(endpoint);
                }
            }
        }

        return targets;
    }

    // This PC's IPv4 networks, from the cards that are up.
    public static IReadOnlyList<(IPAddress Address, IPAddress Mask)> LocalNetworks() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(card => card.OperationalStatus == OperationalStatus.Up
                           && card.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(card => card.GetIPProperties().UnicastAddresses)
            .Where(unicast => unicast.Address.AddressFamily == AddressFamily.InterNetwork && unicast.IPv4Mask is not null)
            .Select(unicast => (unicast.Address, unicast.IPv4Mask))
            .ToList();

    // Every packet to every target. Returns how many sends worked and the
    // last error: some targets can fail (a card that just went down) while
    // the rest still get the packets out.
    public static (int Sent, string? LastError) Send(IReadOnlyCollection<byte[]> macAddresses, IReadOnlyList<IPEndPoint> targets)
    {
        var packets = macAddresses.Select(WakeOnLan.MagicPacket).ToList();
        var sent = 0;
        string? lastError = null;
        using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        for (var repeat = 0; repeat < Repeats; repeat++)
        {
            if (repeat > 0)
            {
                Thread.Sleep(RepeatGap);
            }

            foreach (var packet in packets)
            {
                foreach (var target in targets)
                {
                    try
                    {
                        udp.Send(packet, packet.Length, target);
                        sent++;
                    }
                    catch (SocketException ex)
                    {
                        lastError = ex.Message;
                    }
                }
            }
        }
        return (sent, lastError);
    }
}
