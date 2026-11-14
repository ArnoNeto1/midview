namespace Midview.Protocol;

// "Tocar para encontrar" (MessageType.FindPhone): the payload is two bytes,
// big-endian, the seconds the phone or tablet rings (1 to 300, the Master's
// default 60), or 0 to stop it ringing. Same rules as PhoneFind.kt.
public static class PhoneFind
{
    public const int Stop = 0;
    public const int DefaultSeconds = 60;
    public const int MaxSeconds = 5 * 60;

    public static bool IsValid(int seconds) => seconds is >= Stop and <= MaxSeconds;

    public static byte[] Encode(int seconds)
    {
        if (!IsValid(seconds))
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "de 0 a 300 segundos");
        }
        return new[] { (byte)(seconds >> 8), (byte)seconds };
    }

    // Null for anything but two bytes of 0 to 300.
    public static int? Decode(byte[] payload)
    {
        if (payload.Length != 2)
        {
            return null;
        }
        var seconds = (payload[0] << 8) | payload[1];
        return IsValid(seconds) ? seconds : null;
    }
}
