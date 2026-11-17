using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Midview.Master;

// The lab Wi-Fi (name and password) the "Preparar celular" assistant keeps, asked
// once and remembered. On disk it exists only as a DPAPI CurrentUser blob, like
// the admin password: another Windows user (or PC) can't open it.
public static class PhoneWifiFile
{
    private sealed record Stored(string Ssid, string Password);

    private static string PathIn(string? folder) => Path.Combine(folder ?? MasterData.Folder, "phone-wifi.bin");

    // Null when nothing is saved or the file can't be opened; never throws for those.
    public static (string Ssid, string Password)? Load(string? folder = null)
    {
        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(PathIn(folder)), optionalEntropy: null, DataProtectionScope.CurrentUser);
            var stored = JsonSerializer.Deserialize<Stored>(Encoding.UTF8.GetString(plain));
            return stored is null || string.IsNullOrWhiteSpace(stored.Ssid) ? null : (stored.Ssid, stored.Password ?? "");
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // What is wrong with the password typed for the lab Wi-Fi, or null when it
    // is fine: 8 or more characters, or none at all for an open network (a
    // school's Wi-Fi with a voucher page, like Omada's). The phone takes an
    // empty password as an open network (CableSetupData).
    public static string? PasswordProblem(string password) =>
        password.Length is > 0 and < 8
            ? "A senha do Wi-Fi tem pelo menos 8 caracteres. Numa rede com voucher, deixe em branco: cada aparelho digita o próprio código depois."
            : null;

    public static void Save(string ssid, string password, string? folder = null)
    {
        var path = PathIn(folder);
        var blob = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Stored(ssid, password))), optionalEntropy: null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, blob);
    }
}
