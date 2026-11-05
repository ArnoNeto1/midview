using System.Globalization;
using Microsoft.Win32;

namespace Midview.Protocol;

// One registry value Midview pointed at a lesson's class folder. Key is relative
// to the student's Software key. Values are kept as text: strings as they are,
// DWORD in decimal, binary in base64. Kind is the kind Before is restored with
// (the kind written when Before is null, as the value did not exist).
public sealed record SavedSetting(string Key, string Name, RegistryValueKind Kind, string? Before, string Written);

// The registry values Midview may point at a class folder. The state file is the
// student's to edit and the Agent, running as SYSTEM, undoes from it, so this
// list is a security boundary: no value outside it is ever written or deleted,
// and none through a registry link. What is not guaranteed: a dangling link on
// the way can still have an empty key created where it points (CreateSubKey
// follows it before the handle's name is checked), with no value in it.
public static class ClassSaveTargets
{
    public const string DownloadsKey = @"Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";
    public const string DownloadsName = "{374DE290-123F-4565-9164-39C4925E467B}";

    public const string OfficeRoot = @"Microsoft\Office\16.0";

    // The kind is the one Office itself writes, so the value looks untouched.
    public static readonly (string Key, string Name, RegistryValueKind Kind)[] OfficeFolders =
    {
        (OfficeRoot + @"\Word\Options", "DOC-PATH", RegistryValueKind.ExpandString),
        (OfficeRoot + @"\Excel\Options", "DefaultPath", RegistryValueKind.ExpandString),
        (OfficeRoot + @"\PowerPoint\RecentFolderList", "Default", RegistryValueKind.String),
    };

    // Written as DWORD 0 so Office saves to the local folder instead of the cloud.
    public const string PreferCloudKey = OfficeRoot + @"\Common\General";
    public const string PreferCloudName = "PreferCloudSaveLocations";

    public const string MruKey = @"Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\LastVisitedPidlMRU";
    public static readonly string[] MruPrograms = { "notepad.exe", "mspaint.exe" };

    private enum Family { None, Text, PreferCloud, Mru }

    private static Family FamilyOf(string? key, string? name)
    {
        if (key is null || name is null)
            return Family.None;
        if (Same(key, DownloadsKey) && Same(name, DownloadsName))
            return Family.Text;
        foreach (var folder in OfficeFolders)
        {
            if (Same(key, folder.Key) && Same(name, folder.Name))
                return Family.Text;
        }
        if (Same(key, PreferCloudKey) && Same(name, PreferCloudName))
            return Family.PreferCloud;
        if (Same(key, MruKey) && IsEntryName(name))
            return Family.Mru;
        return Family.None;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // ASCII digits only: \d would also take digits of other scripts.
    private static bool IsEntryName(string name) =>
        name.Length is >= 1 and <= 4 && name.All(c => c is >= '0' and <= '9');

    public static bool IsAllowed(SavedSetting? s)
    {
        if (s is null)
            return false;
        switch (FamilyOf(s.Key, s.Name))
        {
            case Family.Text:
                return IsTextKind(s.Kind) && s.Written is not null;
            case Family.PreferCloud:
                return s.Kind == RegistryValueKind.DWord && IsDWord(s.Written) && (s.Before is null || IsDWord(s.Before));
            case Family.Mru:
                if (s.Kind != RegistryValueKind.Binary || !IsMruEntryOfAProgram(s.Written, null, out var program))
                    return false;
                // What is put back must be an entry of the same program.
                return s.Before is null || IsMruEntryOfAProgram(s.Before, program, out _);
            default:
                return false;
        }
    }

    private static bool IsTextKind(RegistryValueKind kind) =>
        kind is RegistryValueKind.String or RegistryValueKind.ExpandString;

    private static bool IsDWord(string? text) =>
        text is not null && int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);

    private static bool TryDecode(string? text, out byte[] data)
    {
        data = Array.Empty<byte>();
        if (text is null)
            return false;
        try
        {
            data = Convert.FromBase64String(text);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsMruEntryOfAProgram(string? encoded, string? mustBe, out string program)
    {
        program = "";
        if (!TryDecode(encoded, out var entry))
            return false;
        var found = LastVisitedMru.ProgramOf(entry);
        if (found is null)
            return false;
        program = found;
        if (mustBe is not null)
            return Same(found, mustBe);
        return MruPrograms.Any(p => Same(p, found));
    }

    // Null when the key or value does not exist. A value of a kind Midview does
    // not handle (or a REG_DWORD of the wrong size, which a student can plant)
    // also gives null, with its real kind in `kind`; a missing one, or a key that
    // is redirected by a registry link, gives Unknown. ExpandString is read
    // unexpanded, as Downloads is restored from the raw text.
    public static string? ReadEncoded(RegistryKey software, string key, string name, out RegistryValueKind kind)
    {
        kind = RegistryValueKind.Unknown;
        using var subKey = OpenVerified(software, key, writable: false, create: false);
        if (subKey is null)
            return null;
        try
        {
            var value = subKey.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value is null)
                return null;
            var found = subKey.GetValueKind(name);
            kind = found;
            return (found, value) switch
            {
                (RegistryValueKind.String or RegistryValueKind.ExpandString, string text) => text,
                (RegistryValueKind.DWord, int number) => number.ToString(CultureInfo.InvariantCulture),
                (RegistryValueKind.Binary, byte[] bytes) => Convert.ToBase64String(bytes),
                _ => null,
            };
        }
        catch (IOException)
        {
            // The value went away between the two calls: it is missing.
            kind = RegistryValueKind.Unknown;
            return null;
        }
    }

    // Null deletes the value. The key is created when needed, and is left behind
    // by a delete, since it may have been there before. A key reached through a
    // registry link throws InvalidOperationException and nothing is written, so
    // a student cannot send the Agent's write or delete to Policies.
    public static void WriteEncoded(RegistryKey software, string key, string name, RegistryValueKind kind, string? encoded)
    {
        if (encoded is null)
        {
            using var existing = OpenVerified(software, key, writable: true, create: false, throwIfRedirected: true);
            existing?.DeleteValue(name, throwOnMissingValue: false);
            return;
        }

        object value = kind switch
        {
            RegistryValueKind.String or RegistryValueKind.ExpandString => encoded,
            RegistryValueKind.DWord => int.Parse(encoded, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
            RegistryValueKind.Binary => Convert.FromBase64String(encoded),
            _ => throw new ArgumentException($"Unsupported registry kind {kind}.", nameof(kind)),
        };
        using var subKey = OpenVerified(software, key, writable: true, create: true, throwIfRedirected: true)!;
        subKey.SetValue(name, value, kind);
    }

    // Opens `key` one component at a time and checks, on each handle, the name
    // the kernel gives it: it must be its parent's name plus the component. A
    // registry link makes it something else, and the check and the use are on the
    // same handle, so there is no gap to swap a link into. A component that does
    // not exist is created off a parent already checked, so it is never a link.
    private static RegistryKey? OpenVerified(RegistryKey software, string key, bool writable, bool create, bool throwIfRedirected = false)
    {
        var expected = RegistryRedirect.RealName(software);
        var current = software;
        var owned = false;
        foreach (var part in key.Split('\\',StringSplitOptions.RemoveEmptyEntries))
        {
            var next = current.OpenSubKey(part, writable);
            if (next is null && create)
                next = current.CreateSubKey(part, writable);
            if (owned)
                current.Dispose();
            if (next is null)
                return null;
            current = next;
            owned = true;

            expected = expected is null ? null : expected + "\\" + part;
            var real = RegistryRedirect.RealName(current);
            if (expected is null || real is null || !Same(real, expected))
            {
                current.Dispose();
                if (throwIfRedirected)
                    throw new InvalidOperationException($"chave do registro redirecionada: {key}");
                return null;
            }
        }
        return owned ? current : software.OpenSubKey("", writable);
    }

    // Whether the value now is the one Midview wrote, so a value the student
    // changed since is left alone. Text ignores case, a trailing backslash and
    // which of the two string kinds it is (Windows may rewrite one as the other).
    public static bool SameValue(string? current, RegistryValueKind currentKind, SavedSetting s)
    {
        if (current is null)
            return false;
        switch (FamilyOf(s.Key, s.Name))
        {
            case Family.Text:
                return IsTextKind(currentKind) && Same(Trimmed(current), Trimmed(s.Written));
            case Family.PreferCloud:
                return currentKind == RegistryValueKind.DWord && IsDWord(current) && IsDWord(s.Written)
                    && int.Parse(current, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
                        == int.Parse(s.Written, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            case Family.Mru:
                return currentKind == RegistryValueKind.Binary
                    && TryDecode(current, out var now) && TryDecode(s.Written, out var wrote)
                    && now.AsSpan().SequenceEqual(wrote);
            default:
                return false;
        }
    }

    private static string Trimmed(string path) => path.TrimEnd('\\');
}
