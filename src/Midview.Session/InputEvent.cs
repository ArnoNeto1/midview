namespace Midview.Session;

public enum InputEventKind
{
    MouseMove,
    MouseLeftDown,
    MouseLeftUp,
    KeyDown,
    KeyUp,
    MouseRightDown,
    MouseRightUp,
}

public sealed record InputEvent(InputEventKind Kind, int X, int Y, ushort VirtualKeyCode);
