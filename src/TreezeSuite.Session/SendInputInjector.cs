using System.Runtime.InteropServices;

namespace TreezeSuite.Session;

public sealed class SendInputInjector : IInputInjector
{
    public void Inject(InputEvent inputEvent)
    {
        var input = inputEvent.Kind switch
        {
            InputEventKind.MouseMove => MouseInput(inputEvent.X, inputEvent.Y, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE),
            InputEventKind.MouseLeftDown => MouseInput(inputEvent.X, inputEvent.Y, MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_ABSOLUTE),
            InputEventKind.MouseLeftUp => MouseInput(inputEvent.X, inputEvent.Y, MOUSEEVENTF_LEFTUP | MOUSEEVENTF_ABSOLUTE),
            InputEventKind.KeyDown => KeyboardInput(inputEvent.VirtualKeyCode, keyUp: false),
            InputEventKind.KeyUp => KeyboardInput(inputEvent.VirtualKeyCode, keyUp: true),
            _ => throw new ArgumentOutOfRangeException(nameof(inputEvent)),
        };

        var inputs = new[] { input };
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
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
