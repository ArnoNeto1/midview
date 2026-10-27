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
// big-endian; ignored for Cancel), then, only to install the Windows updates
// waiting for a restart on the way (like Windows' "Atualizar e desligar"), a
// flags byte with InstallUpdatesFlag. Without that byte the request is the
// one Agents from before know; with it they refuse it as invalid, rather
// than shut down without the updates. Answered with one CommandAck carrying
// a CommandResult.
public static class PowerCommand
{
    // Enough warning for a class to save and wrap up.
    public const int MaxDelaySeconds = 10 * 60;

    public const byte InstallUpdatesFlag = 0x01;

    public static byte[] Encode(PowerAction action, int delaySeconds, bool installUpdates = false)
    {
        if (delaySeconds is < 0 or > MaxDelaySeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(delaySeconds));
        }
        if (installUpdates && action == PowerAction.Cancel)
        {
            throw new ArgumentException("A cancel has no updates to install.", nameof(installUpdates));
        }
        var payload = new byte[installUpdates ? 4 : 3];
        payload[0] = (byte)action;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(1), (ushort)delaySeconds);
        if (installUpdates)
        {
            payload[3] = InstallUpdatesFlag;
        }
        return payload;
    }

    public static bool TryDecode(byte[] payload, out PowerAction action, out int delaySeconds) =>
        TryDecode(payload, out action, out delaySeconds, out var installUpdates) && !installUpdates;

    public static bool TryDecode(byte[] payload, out PowerAction action, out int delaySeconds, out bool installUpdates)
    {
        action = default;
        delaySeconds = 0;
        installUpdates = false;
        if (payload.Length is not (3 or 4) || !Enum.IsDefined((PowerAction)payload[0]))
        {
            return false;
        }
        // Only the one flag there is, and never on a cancel.
        if (payload.Length == 4 && (payload[3] != InstallUpdatesFlag || (PowerAction)payload[0] == PowerAction.Cancel))
        {
            return false;
        }

        action = (PowerAction)payload[0];
        delaySeconds = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(1));
        installUpdates = payload.Length == 4;
        return delaySeconds <= MaxDelaySeconds;
    }
}
