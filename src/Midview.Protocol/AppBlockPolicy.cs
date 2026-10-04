using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace Midview.Protocol;

public enum AppBlockMode : byte
{
    // Nothing blocked: how a block is lifted.
    None = 0,

    // Only the listed programs are ended when they start.
    BlockList = 1,

    // Everything the student starts is ended, except Windows itself,
    // Midview and the listed programs.
    AllowOnly = 2,
}

// The program-blocking policy the teacher applies (docs/superpowers/specs/
// 2026-09-27-app-blocking-design.md): a mode and a list of normalized exe
// names. SavedAt (Unix seconds, on the teacher's PC) decides between two
// versions, as for the power schedule: the newest wins.
//
// Also the payload of MessageType.SetAppBlock and AppBlockStatus
// (big-endian): format version (1 byte), SavedAt (int64), mode (1 byte),
// count (uint16), then each name as a length byte and its UTF-8 bytes.
public sealed record AppBlockPolicy(long SavedAt, AppBlockMode Mode, IReadOnlyList<string> Programs)
{
    public const byte FormatVersion = 1;
    public const int MaxPrograms = 200;
    public const int MaxNameLength = 64;

    private static readonly Regex ExeNamePattern = new(@"^[a-z0-9 ._()+-]{1,60}\.exe$", RegexOptions.CultureInvariant);

    public static AppBlockPolicy Lifted(long savedAt) => new(savedAt, AppBlockMode.None, Array.Empty<string>());

    public bool IsActive => Mode != AppBlockMode.None;

    // The one form a program name takes in the protocol, on disk and in
    // comparisons: the file name only (a pasted path is accepted), lower
    // case, ".exe" added when missing. Null for anything else (wildcards,
    // paths that end in a folder, control characters, other extensions).
    public static string? NormalizeExeName(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var name = input.Trim().Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..].Trim().ToLowerInvariant();
        if (name.Length > 0 && !name.EndsWith(".exe", StringComparison.Ordinal) && !Path.HasExtension(name))
        {
            name += ".exe";
        }
        return ExeNamePattern.IsMatch(name) ? name : null;
    }

    // Every name normalized and unprotected, no duplicates, none for None.
    public bool IsValid()
    {
        if (!Enum.IsDefined(Mode) || Programs.Count > MaxPrograms || (Mode == AppBlockMode.None && Programs.Count > 0))
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var program in Programs)
        {
            if (NormalizeExeName(program) != program || ProtectedPrograms.IsProtected(program) || !seen.Add(program))
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
            throw new InvalidOperationException("invalid app block policy");
        }

        using var buffer = new MemoryStream();
        Span<byte> header = stackalloc byte[12];
        header[0] = FormatVersion;
        BinaryPrimitives.WriteInt64BigEndian(header[1..], SavedAt);
        header[9] = (byte)Mode;
        BinaryPrimitives.WriteUInt16BigEndian(header[10..], (ushort)Programs.Count);
        buffer.Write(header);
        foreach (var program in Programs)
        {
            var bytes = Encoding.UTF8.GetBytes(program);
            buffer.WriteByte((byte)bytes.Length);
            buffer.Write(bytes);
        }
        return buffer.ToArray();
    }

    public static bool TryDecode(byte[] payload, out AppBlockPolicy policy)
    {
        policy = Lifted(0);
        if (payload.Length < 12 || payload[0] != FormatVersion)
        {
            return false;
        }

        var savedAt = BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(1));
        var mode = (AppBlockMode)payload[9];
        var count = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(10));
        if (count > MaxPrograms)
        {
            return false;
        }

        var programs = new List<string>(count);
        var offset = 12;
        var utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
        for (var i = 0; i < count; i++)
        {
            if (offset >= payload.Length)
            {
                return false;
            }
            var length = payload[offset++];
            if (length == 0 || length > MaxNameLength || offset + length > payload.Length)
            {
                return false;
            }
            try
            {
                programs.Add(utf8.GetString(payload, offset, length));
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
            offset += length;
        }

        var decoded = new AppBlockPolicy(savedAt, mode, programs);
        if (offset != payload.Length || !decoded.IsValid())
        {
            return false;
        }
        policy = decoded;
        return true;
    }

    // Same mode and the same programs, in any order.
    public bool SameRulesAs(AppBlockPolicy other) =>
        Mode == other.Mode && Programs.Count == other.Programs.Count
        && Programs.ToHashSet(StringComparer.Ordinal).SetEquals(other.Programs);
}

// What program blocking never ends, in either mode, even if a teacher types
// one of these names: the shell and the session's own processes, the
// accessibility tools, consent and security prompts, error reporting, and
// Midview. Most of them aren't the student's processes anyway (the Agent
// only ever looks at those); this keeps the desktop alive if another rule is
// wrong. Names are normalized exe names.
public static class ProtectedPrograms
{
    private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
    {
        // Shell and session
        "explorer.exe", "applicationframehost.exe", "startmenuexperiencehost.exe", "shellexperiencehost.exe",
        "searchhost.exe", "searchapp.exe", "textinputhost.exe", "lockapp.exe", "ctfmon.exe", "sihost.exe",
        "taskhostw.exe", "runtimebroker.exe", "dllhost.exe", "conhost.exe", "fontdrvhost.exe", "dwm.exe",
        "csrss.exe", "winlogon.exe", "wininit.exe", "services.exe", "lsass.exe", "smss.exe", "svchost.exe",
        "userinit.exe", "logonui.exe",

        // Accessibility
        "narrator.exe", "osk.exe", "magnify.exe", "atbroker.exe", "utilman.exe",

        // Consent and security prompts
        "consent.exe", "credentialuibroker.exe", "securityhealthsystray.exe", "securityhealthhost.exe",

        // Error reporting
        "werfault.exe", "werfaultsecure.exe",
    };

    public static bool IsProtected(string normalizedExeName) =>
        Names.Contains(normalizedExeName) || normalizedExeName.StartsWith("midview", StringComparison.Ordinal);
}
