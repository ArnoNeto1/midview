using System.Text;

namespace Midview.Protocol;

// "Rede Wi-Fi nova da escola" on the school's phones and tablets: the payload
// of MessageType.AddPhoneWifi is the network's name (1 byte of length, then
// 1 to 32 bytes of UTF-8) and its password (1 byte of length, then none for
// an open network, or 8 to 63 printable ASCII characters, WPA2's rule). The
// device saves it beside the networks it has, so it joins it by itself once
// it is in reach. Same rules as PhoneWifiNetwork.kt.
public static class PhoneWifiNetwork
{
    public const int MaxNameBytes = 32;
    public const int MinPassword = 8;
    public const int MaxPassword = 63;

    public const string NameMissing = "Digite o nome da rede.";
    public const string NameTooLong = "O nome da rede tem no máximo 32 letras.";
    public const string PasswordInvalid =
        "A senha do Wi-Fi tem de 8 a 63 letras, números ou símbolos (sem acentos). Deixe em branco para uma rede aberta.";

    // Null when the name and password can be sent; else what is wrong.
    public static string? Problem(string? name, string? password)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return NameMissing;
        }
        if (Encoding.UTF8.GetByteCount(name) > MaxNameBytes)
        {
            return NameTooLong;
        }
        return IsPassword(password ?? "") ? null : PasswordInvalid;
    }

    private static bool IsPassword(string password) =>
        password.Length == 0
        || (password.Length is >= MinPassword and <= MaxPassword && password.All(c => c is >= ' ' and <= '~'));

    public static byte[] Encode(string name, string? password)
    {
        if (Problem(name, password) is { } problem)
        {
            throw new ArgumentException(problem, nameof(name));
        }
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var passwordBytes = Encoding.ASCII.GetBytes(password ?? "");
        var payload = new byte[2 + nameBytes.Length + passwordBytes.Length];
        payload[0] = (byte)nameBytes.Length;
        nameBytes.CopyTo(payload, 1);
        payload[1 + nameBytes.Length] = (byte)passwordBytes.Length;
        passwordBytes.CopyTo(payload, 2 + nameBytes.Length);
        return payload;
    }

    // Null for a payload that is not one name and one password by the rules.
    public static (string Name, string Password)? Decode(byte[] payload)
    {
        if (payload.Length < 2)
        {
            return null;
        }
        int nameLength = payload[0];
        if (nameLength == 0 || payload.Length < 2 + nameLength)
        {
            return null;
        }
        int passwordLength = payload[1 + nameLength];
        if (payload.Length != 2 + nameLength + passwordLength)
        {
            return null;
        }
        string name;
        try
        {
            name = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(payload, 1, nameLength);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
        var password = Encoding.ASCII.GetString(payload, 2 + nameLength, passwordLength);
        return Problem(name, password) is null ? (name, password) : null;
    }
}
