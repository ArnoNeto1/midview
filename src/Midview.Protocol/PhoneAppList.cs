using System.Buffers.Binary;
using System.Text;

namespace Midview.Protocol;

// One app the student can open on a phone: its package name, the label the
// student sees, the launcher icon as a PNG (48x48 on the phone) and, in the
// list with flags (PhoneAppList.WithFlags), what else the teacher's windows
// need to know of it.
public sealed record PhoneApp(string Package, string Label, byte[] IconPng, PhoneAppFlags Flags = PhoneAppFlags.None);

[Flags]
public enum PhoneAppFlags : byte
{
    None = 0,

    // Came with the phone: a system app, an update of one, or a package the
    // system image has. "Desinstalar programa" leaves it; the teacher blocks
    // it instead.
    System = 1,

    // Suspended now, by the teacher's app block: it does not open.
    Blocked = 2,

    // Installed by the Midview ("Instalar programa"): with System off, the
    // only apps "Desinstalar programa" removes.
    InstalledByMidview = 4,
}

// The phone's answer to GetPhoneApps (MessageType.PhoneApps): the apps the
// student can open, so the teacher can choose which to block, open or
// uninstall. It comes from the student's side, so decoding checks everything
// and a payload that does not decode is refused whole.
//
// Payload (big-endian): format version (1 byte), count (uint16), then each
// app as: package length (1 byte) + package (UTF-8), label length (1 byte) +
// label (UTF-8, at most 64 chars), icon length (uint16) + PNG bytes, and in
// format 2 only, its flags (1 byte, PhoneAppFlags). A teacher gets format 2
// by asking for it (GetPhoneApps with WithFlagsRequest); a phone from before
// it ignores the request and answers format 1, read as flags None.
public static class PhoneAppList
{
    public const byte FormatVersion = 1;
    public const byte WithFlags = 2;

    // GetPhoneApps' payload asking for format 2.
    public static byte[] WithFlagsRequest => new[] { WithFlags };
    public const int MaxApps = 300;
    public const int MaxLabelLength = 64;
    public const int MaxIconBytes = ushort.MaxValue;

    // Throws when the list breaks a bound. format: FormatVersion or WithFlags.
    public static byte[] Encode(IReadOnlyList<PhoneApp> apps, byte format = FormatVersion)
    {
        if (apps.Count > MaxApps || format is not (FormatVersion or WithFlags))
        {
            throw new InvalidOperationException("too many phone apps");
        }

        using var buffer = new MemoryStream();
        buffer.WriteByte(format);
        Span<byte> number = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(number, (ushort)apps.Count);
        buffer.Write(number);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var app in apps)
        {
            if (!PhoneAppPolicy.IsValidPackageName(app.Package) || !seen.Add(app.Package)
                || app.Label is null || app.Label.Length > MaxLabelLength
                || app.IconPng is null || app.IconPng.Length > MaxIconBytes)
            {
                throw new InvalidOperationException("invalid phone app");
            }

            var package = Encoding.UTF8.GetBytes(app.Package);
            buffer.WriteByte((byte)package.Length);
            buffer.Write(package);
            var label = Encoding.UTF8.GetBytes(app.Label);
            buffer.WriteByte((byte)label.Length);
            buffer.Write(label);
            BinaryPrimitives.WriteUInt16BigEndian(number, (ushort)app.IconPng.Length);
            buffer.Write(number);
            buffer.Write(app.IconPng);
            if (format == WithFlags)
            {
                buffer.WriteByte((byte)app.Flags);
            }
        }
        return buffer.ToArray();
    }

    // Null for anything that is not exactly a valid list.
    public static IReadOnlyList<PhoneApp>? Decode(byte[] payload)
    {
        if (payload.Length < 3 || payload[0] is not (FormatVersion or WithFlags))
        {
            return null;
        }
        var withFlags = payload[0] == WithFlags;

        var count = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(1));
        if (count > MaxApps)
        {
            return null;
        }

        var apps = new List<PhoneApp>(count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var offset = 3;
        var utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var package = ReadText(payload, ref offset, utf8);
                var label = ReadText(payload, ref offset, utf8);
                if (package is null || label is null || label.Length > MaxLabelLength
                    || !PhoneAppPolicy.IsValidPackageName(package) || !seen.Add(package)
                    || offset + 2 > payload.Length)
                {
                    return null;
                }

                var iconLength = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(offset));
                offset += 2;
                if (offset + iconLength > payload.Length)
                {
                    return null;
                }
                var icon = payload.AsSpan(offset, iconLength).ToArray();
                offset += iconLength;
                var flags = PhoneAppFlags.None;
                if (withFlags)
                {
                    if (offset >= payload.Length)
                    {
                        return null;
                    }
                    flags = (PhoneAppFlags)payload[offset++];
                }
                apps.Add(new PhoneApp(package, label, icon, flags));
            }
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
        return offset == payload.Length ? apps : null;
    }

    // A one-byte length and that many UTF-8 bytes; null when they do not fit.
    // The caller validates the package (a zero length fails there); an empty
    // label is allowed.
    private static string? ReadText(byte[] payload, ref int offset, UTF8Encoding utf8)
    {
        if (offset >= payload.Length)
        {
            return null;
        }
        var length = payload[offset++];
        if (offset + length > payload.Length)
        {
            return null;
        }
        var text = utf8.GetString(payload, offset, length);
        offset += length;
        return text;
    }
}
