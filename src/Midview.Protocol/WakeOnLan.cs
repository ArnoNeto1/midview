using System.Globalization;

namespace Midview.Protocol;

// Wake-on-LAN: a switched-off machine whose network card is set up for it
// turns on when it sees a "magic packet" -- 6 bytes of 0xFF, then its card's
// MAC address 16 times -- anywhere in a frame on its network. The Master
// broadcasts one (a machine that's off has no IP to send it to), so all it
// needs to know is each machine's MAC address; the Agent tells it (see
// MessageType.GetWakeAddress) and the installer records it.
public static class WakeOnLan
{
    public const int MacAddressLength = 6;
    public const int MagicPacketLength = 6 + 16 * MacAddressLength;

    // The port Wake-on-LAN tools use by convention ("discard"). The card
    // looks for the pattern in any frame, so the port only has to be one
    // nothing on the network minds getting a packet on.
    public const int Port = 9;

    public static byte[] MagicPacket(byte[] macAddress)
    {
        if (!IsUsableMacAddress(macAddress))
        {
            throw new ArgumentException("not a network card's MAC address", nameof(macAddress));
        }

        var packet = new byte[MagicPacketLength];
        packet.AsSpan(0, 6).Fill(0xFF);
        for (var offset = 6; offset < packet.Length; offset += MacAddressLength)
        {
            macAddress.CopyTo(packet, offset);
        }
        return packet;
    }

    // A card's own address: 6 bytes, not all zeros (no address at all), and
    // not a multicast or broadcast one, which no card has as its own.
    public static bool IsUsableMacAddress(ReadOnlySpan<byte> macAddress) =>
        macAddress.Length == MacAddressLength
        && macAddress.IndexOfAnyExcept((byte)0) >= 0
        && (macAddress[0] & 1) == 0;

    // "AA-BB-CC-DD-EE-FF", the way Windows shows it.
    public static string FormatMacAddress(byte[] macAddress)
    {
        if (!IsUsableMacAddress(macAddress))
        {
            throw new ArgumentException("not a network card's MAC address", nameof(macAddress));
        }
        return string.Join("-", macAddress.Select(part => part.ToString("X2", CultureInfo.InvariantCulture)));
    }

    // Accepts "AA-BB-CC-DD-EE-FF", "aa:bb:cc:dd:ee:ff" or "AABBCCDDEEFF".
    public static bool TryParseMacAddress(string? text, out byte[] macAddress)
    {
        macAddress = Array.Empty<byte>();
        var hex = text?.Trim().Replace("-", string.Empty).Replace(":", string.Empty);
        if (hex is not { Length: MacAddressLength * 2 } || !hex.All(char.IsAsciiHexDigit))
        {
            return false;
        }

        var parsed = new byte[MacAddressLength];
        for (var i = 0; i < parsed.Length; i++)
        {
            parsed[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        if (!IsUsableMacAddress(parsed))
        {
            return false;
        }

        macAddress = parsed;
        return true;
    }

    // The stored form of a MAC address from anywhere (a roster file, an
    // installer's machine file), or null if it isn't one.
    public static string? NormalizeMacAddress(string? text) =>
        TryParseMacAddress(text, out var macAddress) ? FormatMacAddress(macAddress) : null;
}
