using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Midview.Protocol;

namespace Midview.Master;

// The school's defaults for the phones and tablets prepared from now on
// ("Pôr sozinho nos próximos preparados", in "Papel de parede" and "Senha
// (PIN)"): the picture for each kind of device (a phone's and a tablet's
// rules differ) and the teacher's PIN. Put on each device the cable just
// prepared as soon as it answers on the network (PreparedDefaults). On disk
// only as a DPAPI CurrentUser blob, like the lab Wi-Fi: the PIN is the
// lock screen's. The phones' admin password ("Senha de administrador dos
// celulares e tablets"): the SetPhoneAdminPassword payload made from it
// (PhoneAdminPassword.Encode, a salted hash), the very bytes each device
// gets, and, so the teacher who forgot it can see it again ("Mostrar a
// senha" in "Senha de administrador"), the password itself, both inside the
// same DPAPI blob. One saved before that has no text (AdminText null).
public static class PhoneDefaultsFile
{
    public sealed record Wallpaper(PhoneWallpaperScreens Screens, byte[] Jpeg);

    // AdminPassword: PhoneAdminPassword.Encode's payload, or null for none;
    // AdminText: the password it was made from, when known.
    public sealed record Defaults(
        string? Pin, Wallpaper? Phone, Wallpaper? Tablet, byte[]? AdminPassword = null, string? AdminText = null)
    {
        public static Defaults None { get; } = new(null, null, null);

        public Wallpaper? For(AndroidDeviceKind kind) => kind == AndroidDeviceKind.Tablet ? Tablet : Phone;

        public bool IsEmpty => Pin is null && Phone is null && Tablet is null && AdminPassword is null;
    }

    private sealed record StoredWallpaper(byte Screens, string Jpeg);

    private sealed record Stored(
        string? Pin, StoredWallpaper? Phone, StoredWallpaper? Tablet, string? AdminPassword = null, string? AdminText = null);

    private static string PathIn(string? folder) => Path.Combine(folder ?? MasterData.Folder, "phone-defaults.bin");

    // None when nothing is saved or the file can't be opened; never throws for those.
    public static Defaults Load(string? folder = null)
    {
        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(PathIn(folder)), optionalEntropy: null, DataProtectionScope.CurrentUser);
            var stored = JsonSerializer.Deserialize<Stored>(Encoding.UTF8.GetString(plain));
            if (stored is null)
            {
                return Defaults.None;
            }
            var admin = AdminPayload(stored.AdminPassword);
            return new Defaults(
                IsPin(stored.Pin) ? stored.Pin : null,
                Picture(stored.Phone),
                Picture(stored.Tablet),
                admin,
                admin is not null && PhoneAdminPassword.IsValid(stored.AdminText) ? stored.AdminText : null);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or JsonException or FormatException)
        {
            return Defaults.None;
        }
    }

    // pin: the 4 digits, or null (or none) to stop putting one.
    public static void SavePin(string? pin, string? folder = null)
    {
        if (string.IsNullOrEmpty(pin))
        {
            pin = null;
        }
        else if (!IsPin(pin))
        {
            throw new ArgumentException(PhoneScreenPassword.Invalid, nameof(pin));
        }
        Save(Load(folder) with { Pin = pin }, folder);
    }

    private static bool IsPin(string? pin) => pin is { Length: > 0 } && PhoneScreenPassword.IsValid(pin);

    // password: the phones' admin password as typed (6 to 12 letters or
    // digits, PhoneAdminPassword.IsValid), or null (or none) to stop putting
    // one. Kept as its salted hash (one salt for every device) and as typed.
    public static void SaveAdminPassword(string? password, string? folder = null)
    {
        byte[]? payload = null;
        if (!string.IsNullOrEmpty(password))
        {
            if (!PhoneAdminPassword.IsValid(password))
            {
                throw new ArgumentException(PhoneAdminPassword.Invalid, nameof(password));
            }
            payload = PhoneAdminPassword.Encode(password);
        }
        Save(Load(folder) with { AdminPassword = payload, AdminText = payload is null ? null : password }, folder);
    }

    private static byte[]? AdminPayload(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return null;
        }
        var payload = Convert.FromBase64String(stored);
        return PhoneAdminPassword.Decode(payload) is null ? null : payload;
    }

    // wallpaper: the picture for that kind of device, or null to stop putting one.
    public static void SaveWallpaper(AndroidDeviceKind kind, Wallpaper? wallpaper, string? folder = null)
    {
        var current = Load(folder);
        Save(kind == AndroidDeviceKind.Tablet ? current with { Tablet = wallpaper } : current with { Phone = wallpaper }, folder);
    }

    // Whether "Papel de parede e PIN da escola" was asked in "Preparar
    // celulares" for that kind (saved or skipped): it is asked only once.
    public static bool Asked(AndroidDeviceKind kind, string? folder = null) => File.Exists(AskedPath(kind, folder));

    public static void MarkAsked(AndroidDeviceKind kind, string? folder = null)
    {
        var path = AskedPath(kind, folder);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    private static string AskedPath(AndroidDeviceKind kind, string? folder) =>
        Path.Combine(folder ?? MasterData.Folder, kind == AndroidDeviceKind.Tablet ? "tablet-defaults-asked.txt" : "phone-defaults-asked.txt");

    // The kinds of device among the window's targets, by their names
    // ("Celular 07", "Tablet 03"); a phone paired by code counts as a phone.
    public static IReadOnlyList<AndroidDeviceKind> KindsOf(IEnumerable<string> names)
    {
        var kinds = names.Select(name => PhoneWipe.TargetOf(name)?.Kind ?? AndroidDeviceKind.Phone).Distinct().OrderBy(kind => kind).ToList();
        return kinds.Count == 0 ? new[] { AndroidDeviceKind.Phone } : kinds;
    }

    // The checkbox in "Papel de parede" and "Senha (PIN)".
    public static string NextOnes(IReadOnlyList<AndroidDeviceKind> kinds) =>
        $"Pôr também nos próximos {(kinds.Count > 1 ? "celulares e tablets" : kinds[0] == AndroidDeviceKind.Tablet ? "tablets" : "celulares")} " +
        "preparados pelo cabo, assim que aparecerem no Midview";

    private static Wallpaper? Picture(StoredWallpaper? stored) =>
        stored is null || stored.Jpeg.Length == 0 || !Enum.IsDefined((PhoneWallpaperScreens)stored.Screens)
            ? null
            : new Wallpaper((PhoneWallpaperScreens)stored.Screens, Convert.FromBase64String(stored.Jpeg));

    private static StoredWallpaper? Stored_(Wallpaper? wallpaper) =>
        wallpaper is null ? null : new StoredWallpaper((byte)wallpaper.Screens, Convert.ToBase64String(wallpaper.Jpeg));

    private static void Save(Defaults defaults, string? folder)
    {
        var path = PathIn(folder);
        if (defaults.IsEmpty)
        {
            File.Delete(path);
            return;
        }
        var json = JsonSerializer.Serialize(new Stored(
            defaults.Pin, Stored_(defaults.Phone), Stored_(defaults.Tablet),
            defaults.AdminPassword is { } admin ? Convert.ToBase64String(admin) : null,
            defaults.AdminPassword is null ? null : defaults.AdminText));
        var blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), optionalEntropy: null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, blob);
    }
}
