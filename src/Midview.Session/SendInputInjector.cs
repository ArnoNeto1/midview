using System.Runtime.InteropServices;
using Midview.Protocol;

namespace Midview.Session;

public sealed class SendInputInjector : IInputInjector
{
    public void Inject(InputEvent inputEvent)
    {
        INPUT? built = inputEvent.Kind switch
        {
            InputEventKind.MouseMove => MouseInput(inputEvent.X, inputEvent.Y, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE),
            InputEventKind.MouseLeftDown => MouseInput(inputEvent.X, inputEvent.Y, MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_ABSOLUTE),
            InputEventKind.MouseLeftUp => MouseInput(inputEvent.X, inputEvent.Y, MOUSEEVENTF_LEFTUP | MOUSEEVENTF_ABSOLUTE),
            InputEventKind.MouseRightDown => MouseInput(inputEvent.X, inputEvent.Y, MOUSEEVENTF_RIGHTDOWN | MOUSEEVENTF_ABSOLUTE),
            InputEventKind.MouseRightUp => MouseInput(inputEvent.X, inputEvent.Y, MOUSEEVENTF_RIGHTUP | MOUSEEVENTF_ABSOLUTE),
            InputEventKind.KeyDown => KeyboardInput(inputEvent.VirtualKeyCode, keyUp: false),
            InputEventKind.KeyUp => KeyboardInput(inputEvent.VirtualKeyCode, keyUp: true),
            // The teacher's wheel delta rides in X (wheel notches, +up/-down).
            InputEventKind.MouseWheel => MouseWheelInput(inputEvent.X),
            // An event kind a newer Master added that this one doesn't know:
            // ignore it instead of dropping the whole control connection.
            _ => (INPUT?)null,
        };

        if (built is not { } input)
        {
            return;
        }
        var inputs = new[] { input };
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    public void TypeText(string text, bool pressEnter)
    {
        var keystrokes = UnicodeKeystrokes.Build(text, pressEnter);
        if (keystrokes.Count == 0)
        {
            return;
        }

        var inputs = new INPUT[keystrokes.Count];
        for (var i = 0; i < inputs.Length; i++)
        {
            var keystroke = keystrokes[i];
            inputs[i] = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = keystroke.VirtualKey,
                        wScan = keystroke.ScanCode,
                        dwFlags = keystroke.Flags,
                    },
                },
            };
        }

        // One call so the whole password arrives uninterrupted by other input.
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT MouseInput(int x, int y, uint flags) => new()
    {
        type = INPUT_MOUSE,
        u = new InputUnion
        {
            mi = new MOUSEINPUT
            {
                dx = NormalizeToAbsolute(x, SystemMetric.SM_CXSCREEN),
                dy = NormalizeToAbsolute(y, SystemMetric.SM_CYSCREEN),
                dwFlags = flags,
            },
        },
    };

    private static INPUT MouseWheelInput(int wheelDelta) => new()
    {
        type = INPUT_MOUSE,
        u = new InputUnion
        {
            mi = new MOUSEINPUT
            {
                // Scrolls the window under the cursor the student already
                // sees moved by the forwarded MouseMove events.
                mouseData = unchecked((uint)wheelDelta),
                dwFlags = MOUSEEVENTF_WHEEL,
            },
        },
    };

    private static INPUT KeyboardInput(ushort virtualKeyCode, bool keyUp) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = virtualKeyCode,
                dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
            },
        },
    };

    private static int NormalizeToAbsolute(int coordinate, SystemMetric metric)
    {
        var screenSize = GetSystemMetrics((int)metric);
        return (int)(coordinate * 65535.0 / screenSize);
    }

    private enum SystemMetric { SM_CXSCREEN = 0, SM_CYSCREEN = 1 }

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, INPUT[] inputs, int structSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
