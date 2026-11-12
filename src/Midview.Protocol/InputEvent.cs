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

    // One UTF-16 unit the teacher typed, in VirtualKeyCode (an emoji comes as
    // its two halves): what a phone types into its focused text field, where
    // a key code alone can't say which character the teacher's keyboard made.
    // Sent to phones only; a Session from before it ignores the kind.
    Text,
}

public sealed record InputEvent(InputEventKind Kind, int X, int Y, ushort VirtualKeyCode);
