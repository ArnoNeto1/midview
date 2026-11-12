using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace Midview.Protocol;

// The app-blocking policy the teacher applies to the school's phones: the
// same layout and modes as AppBlockPolicy, but the names are Android package
// names ("com.whatsapp"), not .exe files. SavedAt (Unix seconds, teacher's
// PC) decides between two versions: the newest wins.
//
// Also the payload of MessageType.SetPhoneAppBlock and PhoneAppBlockStatus
// (big-endian): format version (1 byte), SavedAt (int64), mode (1 byte),
// count (uint16), then each package as a length byte and its UTF-8 bytes.
public sealed record PhoneAppPolicy(long SavedAt, AppBlockMode Mode, IReadOnlyList<string> Packages)
{
    public const byte FormatVersion = 1;
    public const int MaxPackages = 200;
    // Bytes of UTF-8: the length is one byte on the wire.
    public const int MaxNameLength = 255;

    // \z, not $: a trailing newline must not pass.
    private static readonly Regex PackagePattern =
        new(@"^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z0-9_]+)+\z", RegexOptions.CultureInvariant);

    public static PhoneAppPolicy Lifted(long savedAt) => new(savedAt, AppBlockMode.None, Array.Empty<string>());

    public bool IsActive => Mode != AppBlockMode.None;

    public static bool IsValidPackageName(string? name) =>
        name is not null && name.Length <= MaxNameLength && PackagePattern.IsMatch(name);

    // Every name a valid package name, no duplicates, none for None.
    public bool IsValid()
    {
        if (!Enum.IsDefined(Mode) || Packages is null || Packages.Count > MaxPackages || (Mode == AppBlockMode.None && Packages.Count > 0))
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in Packages)
        {
            if (!IsValidPackageName(package) || !seen.Add(package))
            {
                return false;
            }
        }
        return true;
    }

    public byte[] Encode()
    {
        if (!IsValid())
        {
            throw new InvalidOperationException("invalid phone app policy");
        }

        using var buffer = new MemoryStream();
        Span<byte> header = stackalloc byte[12];
        header[0] = FormatVersion;
        BinaryPrimitives.WriteInt64BigEndian(header[1..], SavedAt);
        header[9] = (byte)Mode;
        BinaryPrimitives.WriteUInt16BigEndian(header[10..], (ushort)Packages.Count);
        buffer.Write(header);
        foreach (var package in Packages)
        {
            var bytes = Encoding.UTF8.GetBytes(package);
            buffer.WriteByte((byte)bytes.Length);
            buffer.Write(bytes);
        }
        return buffer.ToArray();
    }

    // Null for anything that is not exactly a valid policy.
    public static PhoneAppPolicy? Decode(byte[] payload)
    {
        if (payload.Length < 12 || payload[0] != FormatVersion)
        {
            return null;
        }

        var savedAt = BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(1));
        var mode = (AppBlockMode)payload[9];
        var count = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(10));
        if (count > MaxPackages)
        {
            return null;
        }

        var packages = new List<string>(count);
        var offset = 12;
        var utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
        for (var i = 0; i < count; i++)
        {
            if (offset >= payload.Length)
            {
                return null;
            }
            var length = payload[offset++];
            if (length == 0 || offset + length > payload.Length)
            {
                return null;
            }
            try
            {
                packages.Add(utf8.GetString(payload, offset, length));
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
            offset += length;
        }

        var decoded = new PhoneAppPolicy(savedAt, mode, packages);
        return offset == payload.Length && decoded.IsValid() ? decoded : null;
    }

    // Same mode and the same packages, in any order.
    public bool SameRulesAs(PhoneAppPolicy other) =>
        Mode == other.Mode && Packages.Count == other.Packages.Count
        && Packages.ToHashSet(StringComparer.Ordinal).SetEquals(other.Packages);
}
