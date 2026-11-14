using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Midview.Master;

// The school's own Mi account (e-mail or phone, and password), used on every
// phone only while it is prepared: Xiaomi allows the cable only with a Mi
// account in, and PhoneCable signs the phone out of it at the end. Asked once
// by the "Preparar celulares" assistant and remembered. On disk it exists
// only as a DPAPI CurrentUser blob, like the lab Wi-Fi: another Windows user
// (or PC) can't open it.
public sealed record MiAccount(string Login, string Password)
{
    // Whether the cable can type the password (MiSignOut.Typed): without
    // accents or "%s". Otherwise the account still serves to sign in, and
    // the sign-out is done by hand.
    public bool CableCanType => MiSignOut.Typed(Password) is not null;
}

public static class MiAccountFile
{
    // Raised after the account is saved or taken off, by whichever window:
    // an open assistant then uses what is saved now.
    public static event Action? Changed;

    private static string PathIn(string? folder) => Path.Combine(folder ?? MasterData.Folder, "mi-account.bin");

    // Null when nothing is saved or the file can't be opened; never throws for those.
    public static MiAccount? Load(string? folder = null)
    {
        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(PathIn(folder)), optionalEntropy: null, DataProtectionScope.CurrentUser);
            var stored = JsonSerializer.Deserialize<MiAccount>(Encoding.UTF8.GetString(plain));
            return stored is null || string.IsNullOrWhiteSpace(stored.Login) || string.IsNullOrEmpty(stored.Password)
                ? null
                : stored;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Save(MiAccount account, string? folder = null)
    {
        var path = PathIn(folder);
        var blob = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(account)), optionalEntropy: null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, blob);
        Changed?.Invoke();
    }

    public static void Clear(string? folder = null)
    {
        var path = PathIn(folder);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        Changed?.Invoke();
    }
}
