namespace Midview.Session;

public readonly record struct Keystroke(ushort VirtualKey, ushort ScanCode, uint Flags);

// Pure builder for the SendInput keystrokes that type text as Unicode, so any
// character (the UAC admin password) types the same regardless of keyboard layout.
public static class UnicodeKeystrokes
{
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_RETURN = 0x0D;

    public static IReadOnlyList<Keystroke> Build(string text, bool pressEnter)
    {
        var keystrokes = new List<Keystroke>(text.Length * 2 + 2);

        // One down/up pair per UTF-16 code unit: a non-BMP character is two
        // surrogate units, which Windows reassembles.
        foreach (var ch in text)
        {
            keystrokes.Add(new Keystroke(0, ch, KEYEVENTF_UNICODE));
            keystrokes.Add(new Keystroke(0, ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
        }

        if (pressEnter)
        {
            // A real virtual key, not Unicode: Enter must act as a key press.
            keystrokes.Add(new Keystroke(VK_RETURN, 0, 0));
            keystrokes.Add(new Keystroke(VK_RETURN, 0, KEYEVENTF_KEYUP));
        }

        return keystrokes;
    }
}
