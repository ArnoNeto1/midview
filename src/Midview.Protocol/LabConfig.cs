using System.Buffers.Binary;

namespace Midview.Protocol;

// The lab-configuration panel's state: which of the browser and Windows
// lockdowns the teacher turned on (docs/superpowers/specs/
// 2026-10-08-lab-configuration-design.md). Each is independent; the console
// shows one button each. SavedAt (Unix seconds on the teacher's PC) decides
// between two versions, as AppBlockPolicy and the power schedule do: the
// newest wins, so two teachers' Masters don't keep overwriting each other.
//
// Also the payload of MessageType.SetLabConfig and LabConfigStatus
// (big-endian): format version (1 byte), SavedAt (int64), flags (1 byte).
public sealed record LabConfig(
    long SavedAt,
    bool CleanOnExit,
    bool Lockdown,
    bool BlockDownloads,
    bool ForceGuest,
    bool Adblock,
    bool BlockEdge,
    bool LockTaskbar)
{
    public const byte FormatVersion = 1;
    private const int PayloadLength = 10;

    // One bit per lockdown, in the flags byte.
    [Flags]
    private enum Flag : byte
    {
        CleanOnExit = 1,
        Lockdown = 2,
        BlockDownloads = 4,
        ForceGuest = 8,
        Adblock = 16,
        BlockEdge = 32,
        LockTaskbar = 64,
    }

    private const Flag KnownFlags =
        Flag.CleanOnExit | Flag.Lockdown | Flag.BlockDownloads | Flag.ForceGuest
        | Flag.Adblock | Flag.BlockEdge | Flag.LockTaskbar;

    // Nothing on: the default, and what a reset leaves.
    public static LabConfig Off(long savedAt) => new(savedAt, false, false, false, false, false, false, false);

    public bool IsAnyOn => CleanOnExit || Lockdown || BlockDownloads || ForceGuest || Adblock || BlockEdge || LockTaskbar;

    public byte[] Encode()
    {
        var buffer = new byte[PayloadLength];
        buffer[0] = FormatVersion;
        BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(1), SavedAt);
        buffer[9] = (byte)Flags();
        return buffer;
    }

    public static bool TryDecode(byte[] payload, out LabConfig config)
    {
        config = Off(0);
        if (payload.Length != PayloadLength || payload[0] != FormatVersion)
        {
            return false;
        }

        var flags = (Flag)payload[9];
        if ((flags & ~KnownFlags) != 0)
        {
            // A bit this version doesn't know: a newer Master against an
            // older Agent shouldn't apply a half-understood policy.
            return false;
        }

        var savedAt = BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(1));
        config = new LabConfig(
            savedAt,
            flags.HasFlag(Flag.CleanOnExit),
            flags.HasFlag(Flag.Lockdown),
            flags.HasFlag(Flag.BlockDownloads),
            flags.HasFlag(Flag.ForceGuest),
            flags.HasFlag(Flag.Adblock),
            flags.HasFlag(Flag.BlockEdge),
            flags.HasFlag(Flag.LockTaskbar));
        return true;
    }

    // Same lockdowns on, in any SavedAt.
    public bool SameLockdownsAs(LabConfig other) => Flags() == other.Flags();

    private Flag Flags()
    {
        var flags = default(Flag);
        if (CleanOnExit) flags |= Flag.CleanOnExit;
        if (Lockdown) flags |= Flag.Lockdown;
        if (BlockDownloads) flags |= Flag.BlockDownloads;
        if (ForceGuest) flags |= Flag.ForceGuest;
        if (Adblock) flags |= Flag.Adblock;
        if (BlockEdge) flags |= Flag.BlockEdge;
        if (LockTaskbar) flags |= Flag.LockTaskbar;
        return flags;
    }
}
