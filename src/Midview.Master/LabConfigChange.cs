using Midview.Protocol;

namespace Midview.Master;

// The read-modify-write behind each lab-panel function button: take the
// machine's current configuration, change the one function, and stamp a
// SavedAt that is always newer than what the machine holds so the change
// wins. Kept apart from the window so it can be tested on its own.
public static class LabConfigChange
{
    // config with just the one function changed. The flag names match the
    // button Tags in LabConfigWindow.xaml.
    public static LabConfig With(LabConfig config, string flag, bool on) => flag switch
    {
        "clean" => config with { CleanOnExit = on },
        "lockdown" => config with { Lockdown = on },
        "downloads" => config with { BlockDownloads = on },
        "guest" => config with { ForceGuest = on },
        "adblock" => config with { Adblock = on },
        "edge" => config with { BlockEdge = on },
        "taskbar" => config with { LockTaskbar = on },
        _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, "unknown lab-config function"),
    };

    // Always strictly newer than the machine's stored SavedAt, so the Agent's
    // newest-wins keeps the teacher's change even if the clocks are off.
    public static long NextSavedAt(long nowUnixSeconds, long currentSavedAt) =>
        Math.Max(nowUnixSeconds, currentSavedAt + 1);
}
