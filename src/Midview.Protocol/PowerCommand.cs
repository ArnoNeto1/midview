using System.Buffers.Binary;

namespace Midview.Protocol;

public enum PowerAction : byte
{
    Shutdown = 0,
    Restart = 1,

    // Calls off a shutdown or restart still counting down.
    Cancel = 2,
}

// Payload of MessageType.Power, an Agent command: the action (one byte),
// then how long the student is warned before it happens, in seconds (uint16,
// big-endian; ignored for Cancel). Answered with one CommandAck carrying a
// CommandResult.
public static class PowerCommand
{
    // Enough warning for a class to save and wrap up.
    public const int MaxDelaySeconds = 10 * 60;

    public static byte[] Encode(PowerAction action, int delaySeconds)
    {
        if (delaySeconds is < 0 or > MaxDelaySeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(delaySeconds));
        }
        var payload = new byte[3];
        payload[0] = (byte)action;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(1), (ushort)delaySeconds);
        return payload;
    }

    public static bool TryDecode(byte[] payload, out PowerAction action, out int delaySeconds)
    {
        action = default;
        delaySeconds = 0;
        if (payload.Length != 3 || !Enum.IsDefined((PowerAction)payload[0]))
        {
            return false;
        }

        action = (PowerAction)payload[0];
        delaySeconds = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(1));
        return delaySeconds <= MaxDelaySeconds;
    }
}
