using System.Buffers.Binary;

namespace Midview.Protocol;

// Payload of MessageType.CloseApps: the mode (one byte), then how long the
// student is warned before their programs close, in seconds (uint16,
// big-endian). A MidviewMaster from before the warning sends the mode alone,
// which means now.
public static class CloseAppsCommand
{
    // The same ceiling as a shutdown's warning.
    public const int MaxDelaySeconds = PowerCommand.MaxDelaySeconds;

    public static byte[] Encode(CloseAppsMode mode, int delaySeconds)
    {
        if (delaySeconds is < 0 or > MaxDelaySeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(delaySeconds));
        }
        var payload = new byte[3];
        payload[0] = (byte)mode;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(1), (ushort)delaySeconds);
        return payload;
    }

    public static bool TryDecode(byte[] payload, out CloseAppsMode mode, out int delaySeconds)
    {
        mode = default;
        delaySeconds = 0;
        if (payload.Length is not (1 or 3) || !Enum.IsDefined((CloseAppsMode)payload[0]))
        {
            return false;
        }

        mode = (CloseAppsMode)payload[0];
        if (payload.Length == 3)
        {
            delaySeconds = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(1));
        }
        return delaySeconds <= MaxDelaySeconds;
    }

    // "30 segundos", "1 minuto", "1 minuto e 30 segundos": as the warning
    // reads to the student and to the teacher.
    public static string DelayText(int delaySeconds)
    {
        var minutes = delaySeconds / 60;
        var seconds = delaySeconds % 60;
        var minutesText = minutes == 1 ? "1 minuto" : $"{minutes} minutos";
        var secondsText = seconds == 1 ? "1 segundo" : $"{seconds} segundos";
        return minutes == 0 ? secondsText
            : seconds == 0 ? minutesText
            : $"{minutesText} e {secondsText}";
    }
}
