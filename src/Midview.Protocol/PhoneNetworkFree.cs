namespace Midview.Protocol;

// "Liberar Wi-Fi e modo avião" on the school's phones: the payload of
// MessageType.FreePhoneNetwork is one byte, the minutes (1 to 60) the
// student may change the Wi-Fi and airplane mode before the agent locks
// them again by itself, or 0 to lock them again now. Same rules as
// PhoneNetworkFree.kt.
public static class PhoneNetworkFree
{
    public const int LockNow = 0;
    public const int MaxMinutes = 60;

    // What the teacher picks from in the Master.
    public static readonly IReadOnlyList<int> Choices = new[] { 10, 30, 60 };

    public const int DefaultMinutes = 10;

    public static bool IsValid(int minutes) => minutes is >= LockNow and <= MaxMinutes;

    public static byte[] Encode(int minutes)
    {
        if (!IsValid(minutes))
        {
            throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "de 0 a 60 minutos");
        }
        return new[] { (byte)minutes };
    }

    // Null for anything but one byte of 0 to 60.
    public static int? Decode(byte[] payload) =>
        payload.Length == 1 && payload[0] <= MaxMinutes ? payload[0] : null;
}
