using System.Text;

namespace Midview.Protocol;

// The teacher's lock screen PIN for the school's phones: the payload of
// MessageType.SetPhoneScreenPassword is the PIN's ASCII digits, exactly 4 of
// them, or nothing (empty) to remove it. The Android agent sets it as a PIN
// and keeps it there (the student can neither change nor remove it). Same
// rules as PhoneScreenPassword.kt.
public static class PhoneScreenPassword
{
    public const int PinLength = 4;

    // No PIN at all.
    public const string None = "";

    // What the teacher reads for anything else (the agent says the same).
    public const string Invalid = "o PIN tem que ter 4 números";

    public static bool IsValid(string? pin) =>
        pin is not null && (pin.Length == 0 || (pin.Length == PinLength && pin.All(c => c is >= '0' and <= '9')));

    public static byte[] Encode(string pin)
    {
        if (!IsValid(pin))
        {
            throw new ArgumentException(Invalid, nameof(pin));
        }
        return Encoding.ASCII.GetBytes(pin);
    }

    // Null for anything that is not exactly 4 ASCII digits or empty.
    public static string? Decode(byte[] payload)
    {
        if (payload.Length != 0 && (payload.Length != PinLength || payload.Any(b => b is < (byte)'0' or > (byte)'9')))
        {
            return null;
        }
        return Encoding.ASCII.GetString(payload);
    }
}
