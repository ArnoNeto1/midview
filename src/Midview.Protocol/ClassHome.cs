using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace Midview.Protocol;

// Where the class folders live when a lab has two teacher PCs ("Pasta da
// turma", docs/superpowers/specs/2026-10-20-class-folder-design.md, "Two
// teacher PCs in one lab"): the PC whose Master started the first lesson, which
// every Agent records. Two teacher PCs can share one teacher identity (one
// certificate), so the thumbprint alone doesn't tell them apart: the identity
// is the thumbprint and the computer name. Address and Port are where the
// colleague's PC reaches the home's Master (ClassFolderRemote.Port).
//
// Generation is when that PC became the home (UTC ticks, on its own clock),
// what it claims with ever after: a PC takes the folders over only with a
// newer one, so a home that took them over while some PCs were off isn't
// undone by the old home once they are back, and the newest home wins. 0 for
// a home an Agent recorded from a lesson start with no claim (a Master from
// before two teacher PCs).
//
// A ClassHome is the payload of MessageType.ClassHomeStatus (big-endian):
// format version (1 byte), thumbprint (uint16 byte length, then ASCII hex),
// name (uint16 byte length, then UTF-8), address (uint16 byte length, then
// UTF-8), port (uint16), generation (int64). An empty payload is "no home"
// (None).
public sealed record ClassHome(string Thumbprint, string Name, string Address, int Port, long Generation)
{
    public const byte FormatVersion = 2;

    // In characters. A computer name is at most 63.
    public const int MaxNameLength = 64;

    // In characters: an IPv6 address with a scope is at most 56.
    public const int MaxAddressLength = 64;

    // What a refusal names for a home recorded with no name (a Master that
    // sent no claim): "As pastas das turmas ficam no PC de outro professor".
    public const string UnnamedText = "de outro professor";

    // No home: what an Agent has before any lesson claimed it.
    public static ClassHome? None => null;

    // Is this the home's own PC? The thumbprint and the name, each ignoring
    // case. A home recorded with no name (for a Master that sent no claim)
    // is any PC with that thumbprint; the first claim fills the name in.
    public bool IsSameIdentity(string thumbprint, string name) =>
        string.Equals(Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase)
        && (Name.Length == 0 || string.Equals(Name, name, StringComparison.OrdinalIgnoreCase));

    // Is the PC with that certificate thumbprint, computer name and generation
    // (null: it never was the home) the home's own PC? Its identity
    // (IsSameIdentity); or its certificate and the home's generation under a
    // new computer name (the PC was renamed); or the home's name under another
    // certificate (Midview reinstalled or paired again, or another Windows
    // account on that PC: each has its own teacher identity).
    public bool IsSamePc(string thumbprint, string name, long? generation) =>
        IsSameIdentity(thumbprint, name)
        || (generation == Generation && string.Equals(Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
        || (Name.Length > 0 && string.Equals(Name, name, StringComparison.OrdinalIgnoreCase));

    // What an Agent answers a claim, or a lesson start, from a PC that isn't
    // the home: ClassHomeClaim.OtherHomeText, then the home's name.
    public string RefusalText => ClassHomeClaim.OtherHomeText + (Name.Length > 0 ? Name : UnnamedText);

    // What Encode writes and TryDecode accept.
    internal bool IsWellFormed =>
        TeacherInvite.IsThumbprint(Thumbprint)
        && ClassHomeWire.IsName(Name)
        && Address is { Length: > 0 and <= MaxAddressLength }
        && IPAddress.TryParse(Address, out _)
        && ClassHomeWire.IsPort(Port)
        && Generation >= 0;

    public byte[] Encode()
    {
        if (!IsWellFormed)
        {
            throw new InvalidOperationException(
                "The class home can't be sent: the thumbprint is 40 hex digits, "
                + $"the name at most {MaxNameLength} characters, the address an IP address, the port 1 to 65535 "
                + "and the generation not below 0.");
        }

        using var stream = new MemoryStream();
        stream.WriteByte(FormatVersion);
        ClassHomeWire.PutText(stream, Thumbprint);
        ClassHomeWire.PutText(stream, Name);
        ClassHomeWire.PutText(stream, Address);
        ClassHomeWire.PutUInt16(stream, Port);
        ClassHomeWire.PutInt64(stream, Generation);
        return stream.ToArray();
    }

    // What a ClassHomeStatus carries for a home that may not exist.
    public static byte[] Encode(ClassHome? home) => home?.Encode() ?? Array.Empty<byte>();

    // False for a payload that isn't one. True with a null home for an empty
    // payload: the Agent has no home.
    public static bool TryDecode(byte[] payload, out ClassHome? home)
    {
        home = None;
        if (payload.Length == 0)
        {
            return true;
        }

        var position = 0;
        if (payload[position++] != FormatVersion
            || !ClassHomeWire.TryText(payload, ref position, out var thumbprint)
            || !ClassHomeWire.TryText(payload, ref position, out var name)
            || !ClassHomeWire.TryText(payload, ref position, out var address)
            || !ClassHomeWire.TryUInt16(payload, ref position, out var port)
            || !ClassHomeWire.TryInt64(payload, ref position, out var generation)
            || position != payload.Length)
        {
            return false;
        }

        var decoded = new ClassHome(thumbprint, name, address, port, generation);
        if (!decoded.IsWellFormed)
        {
            return false;
        }

        home = decoded;
        return true;
    }
}

// What a teacher PC sends an Agent before it starts a lesson: "I am the home".
// Name is the PC's computer name, Port where its Master listens
// (ClassFolderRemote.Port), Generation when it became the home (ClassHome.
// Generation, above 0); the Agent takes the thumbprint from the TLS channel,
// never from the payload, and the address from the connection. TakeOver says
// this PC takes the class folders from another home, which an Agent allows
// only with a newer generation than that home's, and with no lesson on.
//
// The payload of MessageType.ClaimClassHome (big-endian): format version
// (1 byte), flags (1 byte, bit 0 TakeOver, no other bit set), port (uint16),
// generation (int64), name (uint16 byte length, then UTF-8).
public sealed record ClassHomeClaim(string Name, int Port, bool TakeOver, long Generation)
{
    public const byte FormatVersion = 2;

    // How an Agent's refusal of a claim starts when another PC is the home;
    // the home's name follows. The Master recognises it with StartsWith.
    public const string OtherHomeText = "As pastas das turmas ficam no PC ";

    private const byte TakeOverFlag = 1;

    // What Encode writes and TryDecode accept.
    internal bool IsWellFormed => ClassHomeWire.IsName(Name) && ClassHomeWire.IsPort(Port) && Generation > 0;

    public byte[] Encode()
    {
        if (!IsWellFormed)
        {
            throw new InvalidOperationException(
                $"The class home claim can't be sent: the name is at most {ClassHome.MaxNameLength} characters, "
                + "the port 1 to 65535 and the generation above 0.");
        }

        using var stream = new MemoryStream();
        stream.WriteByte(FormatVersion);
        stream.WriteByte(TakeOver ? TakeOverFlag : (byte)0);
        ClassHomeWire.PutUInt16(stream, Port);
        ClassHomeWire.PutInt64(stream, Generation);
        ClassHomeWire.PutText(stream, Name);
        return stream.ToArray();
    }

    public static bool TryDecode(byte[] payload, out ClassHomeClaim? claim)
    {
        claim = null;
        if (payload.Length < 2 || payload[0] != FormatVersion || payload[1] > TakeOverFlag)
        {
            return false;
        }

        var position = 2;
        if (!ClassHomeWire.TryUInt16(payload, ref position, out var port)
            || !ClassHomeWire.TryInt64(payload, ref position, out var generation)
            || !ClassHomeWire.TryText(payload, ref position, out var name)
            || position != payload.Length)
        {
            return false;
        }

        var decoded = new ClassHomeClaim(name, port, payload[1] == TakeOverFlag, generation);
        if (!decoded.IsWellFormed)
        {
            return false;
        }

        claim = decoded;
        return true;
    }
}

// What ClassHome and ClassHomeClaim write and read: a string is a uint16 byte
// length, then strict UTF-8; a number is big-endian. Every Try* is false when
// what is left is too short or not valid.
internal static class ClassHomeWire
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    public static bool IsName(string name) => name is not null && name.Length <= ClassHome.MaxNameLength;

    public static bool IsPort(int port) => port is >= 1 and <= ushort.MaxValue;

    public static void PutText(Stream stream, string text)
    {
        var bytes = StrictUtf8.GetBytes(text);
        PutUInt16(stream, bytes.Length);
        stream.Write(bytes);
    }

    public static void PutUInt16(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, checked((ushort)value));
        stream.Write(buffer);
    }

    public static void PutInt64(Stream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        stream.Write(buffer);
    }

    public static bool TryInt64(byte[] payload, ref int position, out long value)
    {
        value = 0;
        if (payload.Length - position < 8)
        {
            return false;
        }

        value = BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(position));
        position += 8;
        return true;
    }

    public static bool TryUInt16(byte[] payload, ref int position, out int value)
    {
        value = 0;
        if (payload.Length - position < 2)
        {
            return false;
        }

        value = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(position));
        position += 2;
        return true;
    }

    public static bool TryText(byte[] payload, ref int position, out string text)
    {
        text = "";
        if (!TryUInt16(payload, ref position, out var length) || payload.Length - position < length)
        {
            return false;
        }

        try
        {
            text = StrictUtf8.GetString(payload, position, length);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        position += length;
        return true;
    }
}
