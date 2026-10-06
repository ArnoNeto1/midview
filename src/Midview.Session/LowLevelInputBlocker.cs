using System.Runtime.InteropServices;

namespace Midview.Session;

// Locks the student's keyboard and mouse by swallowing their physical input,
// with low-level hooks on a thread of their own. Input this process sends
// while the teacher controls the machine (SendInputInjector) is marked
// injected by Windows and goes through. It replaced disabling the devices
// through the Agent after the first live run (ALUNO-25, PS/2): Windows
// reported success but left the keyboard working until a restart, and once
// disabled the mouse stayed so until the machine was restarted. Hooks work
// whatever the keyboard or mouse is, come off at once, and go with this
// process however it ends, so a lock can never outlive it. Ctrl+Alt+Del
// can't be blocked this way (nor any other way short of a driver).
public sealed class LowLevelInputBlocker : IInputDeviceController
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const uint LLKHF_INJECTED = 0x10;
    private const uint LLMHF_INJECTED = 0x01;
    private const uint WM_QUIT = 0x0012;
    private const uint PM_NOREMOVE = 0x0000;

    private readonly object _gate = new();
    private uint _hookThreadId;

    public void DisableInputDevices()
    {
        lock (_gate)
        {
            if (_hookThreadId != 0)
            {
                return;
            }

            using var started = new ManualResetEventSlim();
            uint threadId = 0;
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                // Kept alive by this thread's frame for as long as the hooks are installed.
                HookProc keyboard = (code, message, data) =>
                    code >= 0 && (Marshal.ReadInt32(data, 8) & LLKHF_INJECTED) == 0
                        ? (IntPtr)1
                        : CallNextHookEx(IntPtr.Zero, code, message, data);
                HookProc mouse = (code, message, data) =>
                    code >= 0 && (Marshal.ReadInt32(data, 12) & LLMHF_INJECTED) == 0
                        ? (IntPtr)1
                        : CallNextHookEx(IntPtr.Zero, code, message, data);

                var module = GetModuleHandle(null);
                var keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyboard, module, 0);
                var mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouse, module, 0);
                if (keyboardHook == IntPtr.Zero || mouseHook == IntPtr.Zero)
                {
                    failure = new InvalidOperationException($"SetWindowsHookEx failed ({Marshal.GetLastWin32Error()})");
                }
                threadId = GetCurrentThreadId();
                PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE); // the queue EnableInputDevices posts its quit to
                started.Set();

                // Hooks are called through this thread's message loop.
                while (failure is null && GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
                {
                }

                if (keyboardHook != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(keyboardHook);
                }
                if (mouseHook != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(mouseHook);
                }
                GC.KeepAlive(keyboard);
                GC.KeepAlive(mouse);
            })
            {
                IsBackground = true,
                Name = "Input blocker",
            };
            thread.Start();
            started.Wait();

            if (failure is not null)
            {
                throw failure;
            }
            _hookThreadId = threadId;
        }
    }

    public void EnableInputDevices()
    {
        lock (_gate)
        {
            if (_hookThreadId == 0)
            {
                return;
            }
            PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _hookThreadId = 0;
        }
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookId, HookProc proc, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG message, IntPtr window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG message, IntPtr window, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
