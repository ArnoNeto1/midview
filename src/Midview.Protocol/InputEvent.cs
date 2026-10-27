namespace Midview.Protocol;

// An InputEvent message carries the kind as its number (one byte, see
// InputEventCodec), not its name: keep the members in this order and add new
// ones at the end.
public enum InputEventKind
{
    MouseMove,
    MouseLeftDown,
    MouseLeftUp,
    KeyDown,
    KeyUp,
    MouseRightDown,
    MouseRightUp,
    MouseWheel,
}

public sealed record InputEvent(InputEventKind Kind, int X, int Y, ushort VirtualKeyCode);
