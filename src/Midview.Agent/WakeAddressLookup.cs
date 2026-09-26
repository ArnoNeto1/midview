using System.Net;
using System.Net.NetworkInformation;
using Midview.Protocol;

namespace Midview.Agent;

// Answers MessageType.GetWakeAddress: which network card's MAC address the
// Master should use to turn this machine on. The card a Master's connection
// arrived through is, by definition, one on the teacher's network -- unlike
// guessing among Wi-Fi, VPN and virtual adapters.
public static class WakeAddressLookup
{
    public sealed record NetworkCard(IReadOnlyCollection<IPAddress> Addresses, byte[] MacAddress);

    public static byte[]? Find(IPAddress localAddress)
    {
        try
        {
            return Find(localAddress, AllNetworkCards());
        }
        catch (NetworkInformationException ex)
        {
            AgentLog.Warn($"Couldn't list the network cards to find this machine's MAC address: {ex.Message}");
            return null;
        }
    }

    // Null when no card has that address, or the one that does has no usable
    // MAC address (loopback, some tunnels).
    public static byte[]? Find(IPAddress localAddress, IEnumerable<NetworkCard> cards)
    {
        var address = localAddress.IsIPv4MappedToIPv6 ? localAddress.MapToIPv4() : localAddress;
        var card = cards.FirstOrDefault(card => card.Addresses.Contains(address));
        return card is not null && WakeOnLan.IsUsableMacAddress(card.MacAddress) ? card.MacAddress : null;
    }

    private static IEnumerable<NetworkCard> AllNetworkCards() =>
        NetworkInterface.GetAllNetworkInterfaces().Select(card => new NetworkCard(
            card.GetIPProperties().UnicastAddresses.Select(unicast => unicast.Address).ToList(),
            card.GetPhysicalAddress().GetAddressBytes()));
}
