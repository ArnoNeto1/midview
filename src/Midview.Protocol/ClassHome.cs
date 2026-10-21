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
// A ClassHome is the payload of MessageType.ClassHomeStatus (big-endian):
// format version (1 byte), thumbprint (uint16 byte length, then ASCII hex),
// name (uint16 byte length, then UTF-8), address (uint16 byte length, then
// UTF-8), port (uint16). An empty payload is "no home" (None).
public sealed record ClassHome(string Thumbprint, string Name, string Address, int Port)
{
    public const byte FormatVersion = 1;

    // In characters. A computer name is at most 63.
    public const int MaxNameLength = 64;

    // In characters: an IPv6 address with a scope is at most 56.
    public const int MaxAddressLength = 64;

    // No home: what an Agent has before any lesson claimed it.
    public static ClassHome? None => null;

    // Is this the home's own PC? The thumbprint and the name, each ignoring
    // case. A home recorded with no name (for a Master that sent no claim)
    // is any PC with that thumbprint; the first claim fills the name in.
    public bool IsSameIdentity(string thumbprint, string name) =>
        string.Equals(Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase)
        && (Name.Length == 0 || string.Equals(Name, name, StringComparison.OrdinalIgnoreCase));

    // What Encode writes and TryDecode accept.
    internal bool IsWellFormed =>
        TeacherInvite.IsThumbprint(Thumbprint)
        && ClassHomeWire.IsName(Name)
        && Address is { Length: > 0 and <= MaxAddressLength }
        && IPAddress.TryParse(Address, out _)
        && ClassHomeWire.IsPort(Port);

    public byte[] Encode()
    {
        if (!IsWellFormed)
        {
            throw new InvalidOperationException(
                "The class home can't be sent: the thumbprint is 40 hex digits, "
                + $"the name at most {MaxNameLength} characters, the address an IP address and the port 1 to 65535.");
        }

        using var stream = new MemoryStream();
        stream.WriteByte(FormatVersion);
        ClassHomeWire.PutText(stream, Thumbprint);
        ClassHomeWire.PutText(stream, Name);
        ClassHomeWire.PutText(stream, Address);
        ClassHomeWire.PutUInt16(stream, Port);
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
            || position != payload.Length)
        {
            return false;
        }

        var decoded = new ClassHome(thumbprint, name, address, port);
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
// (ClassFolderRemote.Port); the Agent takes the thumbprint from the TLS
// channel, never from the payload, and the address from the connection.
// TakeOver says the teacher chose to keep the class folders on this PC although
// another one is the home.
//
// The payload of MessageType.ClaimClassHome (big-endian): format version
// (1 byte), flags (1 byte, bit 0 TakeOver, no other bit set), port (uint16),
// name (uint16 byte length, then UTF-8).
public sealed record ClassHomeClaim(string Name, int Port, bool TakeOver)
{
    public const byte FormatVersion = 1;

    // How an Agent's refusal of a claim starts when another PC is the home;
    // the home's name follows. The Master recognises it with StartsWith.
    public const string OtherHomeText = "As pastas das turmas ficam no PC ";

    private const byte TakeOverFlag = 1;

    // What Encode writes and TryDecode accept.
    internal bool IsWellFormed => ClassHomeWire.IsName(Name) && ClassHomeWire.IsPort(Port);

    public byte[] Encode()
    {
        if (!IsWellFormed)
        {
            throw new InvalidOperationException(
                $"The class home claim can't be sent: the name is at most {ClassHome.MaxNameLength} characters and the port 1 to 65535.");
        }

        using var stream = new MemoryStream();
        stream.WriteByte(FormatVersion);
        stream.WriteByte(TakeOver ? TakeOverFlag : (byte)0);
        ClassHomeWire.PutUInt16(stream, Port);
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
            || !ClassHomeWire.TryText(payload, ref position, out var name)
            || position != payload.Length)
        {
            return false;
        }

        var decoded = new ClassHomeClaim(name, port, payload[1] == TakeOverFlag);
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
