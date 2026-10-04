using System.Buffers.Binary;
using System.Text;

namespace Midview.Protocol;

// One program the student has open, as the Session reports it for the
// teacher's "block programs" window: its normalized exe name, a name to
// show (the file's description, or the exe name) and how many windows it
// has open.
public sealed record OpenProgram(string ExeName, string DisplayName, int WindowCount);

// Payload of MessageType.OpenPrograms, the answer to ListOpenPrograms
// (big-endian): format version (1 byte), count (uint16), then per program
// the exe name and the display name (each a length byte and UTF-8) and the
// window count (uint16). The report only suggests names to the teacher, but
// it comes from the student's side: decoding checks everything, and a
// report that doesn't decode is refused whole.
public static class OpenProgramsReport
{
    public const byte FormatVersion = 1;
    public const int MaxPrograms = 100;
    public const int MaxDisplayNameLength = 64;

    public static byte[] Encode(IEnumerable<OpenProgram> programs)
    {
        var list = programs.Take(MaxPrograms).ToList();
        using var buffer = new MemoryStream();
        buffer.WriteByte(FormatVersion);
        Span<byte> number = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(number, (ushort)list.Count);
        buffer.Write(number);
        foreach (var program in list)
        {
            WriteText(buffer, program.ExeName);
            WriteText(buffer, CleanDisplayName(program.DisplayName, program.ExeName));
            BinaryPrimitives.WriteUInt16BigEndian(number, (ushort)Math.Clamp(program.WindowCount, 0, ushort.MaxValue));
            buffer.Write(number);
        }
        return buffer.ToArray();
    }

    public static bool TryDecode(byte[] payload, out IReadOnlyList<OpenProgram> programs)
    {
        programs = Array.Empty<OpenProgram>();
        if (payload.Length < 3 || payload[0] != FormatVersion)
        {
            return false;
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(1));
        if (count > MaxPrograms)
        {
            return false;
        }

        var offset = 3;
        var decoded = new List<OpenProgram>(count);
        for (var i = 0; i < count; i++)
        {
            if (!TryReadText(payload, ref offset, out var exeName) || !TryReadText(payload, ref offset, out var displayName)
                || offset + 2 > payload.Length)
            {
                return false;
            }
            var windows = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(offset));
            offset += 2;

            if (AppBlockPolicy.NormalizeExeName(exeName) != exeName || CleanDisplayName(displayName, exeName) != displayName)
            {
                return false;
            }
            decoded.Add(new OpenProgram(exeName, displayName, windows));
        }

        if (offset != payload.Length)
        {
            return false;
        }
        programs = decoded;
        return true;
    }

    // One line of plain text: control characters turned into spaces,
    // trimmed, at most MaxDisplayNameLength characters; the exe name when
    // nothing is left.
    public static string CleanDisplayName(string? displayName, string exeName)
    {
        var text = new string((displayName ?? string.Empty).Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        if (text.Length > MaxDisplayNameLength)
        {
            var cut = char.IsHighSurrogate(text[MaxDisplayNameLength - 1]) ? MaxDisplayNameLength - 1 : MaxDisplayNameLength;
            text = text[..cut].TrimEnd();
        }
        return text.Length == 0 ? exeName : text;
    }

    private static void WriteText(Stream buffer, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > byte.MaxValue)
        {
            throw new ArgumentException("text too long", nameof(text));
        }
        buffer.WriteByte((byte)bytes.Length);
        buffer.Write(bytes);
    }

    private static bool TryReadText(byte[] payload, ref int offset, out string text)
    {
        text = string.Empty;
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
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(payload, offset, length);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        offset += length;
        return true;
    }
}
