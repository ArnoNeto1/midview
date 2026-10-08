using System.Buffers.Binary;
using System.Text;

namespace Midview.Protocol;

// How far "do dia" reaches, for ClearDownloads (its 1-byte payload).
public enum DownloadsScope : byte
{
    // Only files changed today (local time on the student's machine).
    Today = 0,

    // Everything in each user's Downloads folder.
    All = 1,
}

// A list of program names, both ways on the wire: the Agent's answer to
// GetInstalledPrograms (MessageType.InstalledPrograms -- the programs that
// can be uninstalled) and the teacher's UninstallPrograms command (the ones
// to uninstall). The names are the Windows "display names" ("Google Chrome",
// "7-Zip 23.01"). It comes from, or goes to, the student's side, so decoding
// checks everything and a payload that doesn't decode is refused whole.
//
// Payload (big-endian): format version (1 byte), count (uint16), then each
// name as a length byte and its UTF-8 bytes.
public static class ProgramList
{
    public const byte FormatVersion = 1;
    public const int MaxPrograms = 1000;
    public const int MaxNameLength = 255;

    public static byte[] Encode(IEnumerable<string> names)
    {
        var list = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Take(MaxPrograms)
            .ToList();

        using var buffer = new MemoryStream();
        buffer.WriteByte(FormatVersion);
        Span<byte> number = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(number, (ushort)list.Count);
        buffer.Write(number);
        foreach (var name in list)
        {
            var bytes = Encoding.UTF8.GetBytes(Fit(name));
            buffer.WriteByte((byte)bytes.Length);
            buffer.Write(bytes);
        }
        return buffer.ToArray();
    }

    // Shortened to at most MaxNameLength UTF-8 bytes without splitting a
    // character, so the strict decoder on the other side never rejects the
    // whole list over one over-long name.
    private static string Fit(string name)
    {
        if (Encoding.UTF8.GetByteCount(name) <= MaxNameLength)
        {
            return name;
        }
        var kept = name.Length;
        while (kept > 0 && Encoding.UTF8.GetByteCount(name.AsSpan(0, kept)) > MaxNameLength)
        {
            kept--;
        }
        return name[..kept];
    }

    public static bool TryDecode(byte[] payload, out IReadOnlyList<string> names)
    {
        names = Array.Empty<string>();
        if (payload.Length < 3 || payload[0] != FormatVersion)
        {
            return false;
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(1));
        if (count > MaxPrograms)
        {
            return false;
        }

        var result = new List<string>(count);
        var offset = 3;
        var utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
        for (var i = 0; i < count; i++)
        {
            if (offset >= payload.Length)
            {
                return false;
            }
            var length = payload[offset++];
            if (length == 0 || offset + length > payload.Length)
            {
                return false;
            }
            try
            {
                result.Add(utf8.GetString(payload, offset, length));
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
            offset += length;
        }

        if (offset != payload.Length)
        {
            return false;
        }
        names = result;
        return true;
    }
}
