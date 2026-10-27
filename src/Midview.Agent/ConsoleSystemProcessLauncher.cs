using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Midview.Agent;

// Launches the Desk helper into the active console session with a copy of the
// Agent's own token, kept as SYSTEM. Unlike UserSessionProcessLauncher, which
// runs Session as the logged-on student, the helper must stay SYSTEM so it can
// switch to and drive the secure (UAC) desktop, which a student's token cannot
// reach. The recipe: duplicate the Agent's process token into a primary token,
// move it to the console session, and CreateProcessAsUser onto "winsta0\default".
//
// This requires SeTcbPrivilege ("Act as part of the operating system"): placing
// a token in another session and launching across sessions needs it. LocalSystem
// holds it enabled by default, so the launcher must run from the SYSTEM service;
// from any ordinary account the move or the cross-session launch fails.
public sealed class ConsoleSystemProcessLauncher : ISessionProcessLauncher
{
    private readonly string _deskExecutablePath;
    private readonly Func<bool> _someoneLoggedOn;

    public ConsoleSystemProcessLauncher(string deskExecutablePath)
        : this(deskExecutablePath, UserSessionProcessLauncher.SomeoneLoggedOn)
    {
    }

    // The logged-on gate is injectable so the "nobody logged on" refusal can be
    // tested without a real console session; it defaults to the real check.
    internal ConsoleSystemProcessLauncher(string deskExecutablePath, Func<bool> someoneLoggedOn)
    {
        _deskExecutablePath = deskExecutablePath;
        _someoneLoggedOn = someoneLoggedOn;
    }

    public bool TryLaunch(out int processId)
    {
        processId = 0;

        // Nobody at the console: there is no desktop to show the helper on, so
        // start nothing.
        if (!_someoneLoggedOn())
        {
            return false;
        }

        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF)
        {
            return false; // no active console session (e.g. sitting at the logon screen)
        }

        // The Agent's own token. Running as the SYSTEM service this is the
        // LocalSystem token, which holds SeTcbPrivilege -- needed to move the
        // duplicated token into another session and launch there.
        if (!OpenProcessToken(GetCurrentProcess(),
                TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ASSIGN_PRIMARY, out var token))
        {
            return false;
        }

        try
        {
            if (!DuplicateTokenEx(token, MAXIMUM_ALLOWED, IntPtr.Zero,
                    SECURITY_IMPERSONATION_LEVEL.SecurityIdentification, TOKEN_TYPE.TokenPrimary, out var primaryToken))
            {
                return false;
            }

            try
            {
                // Move the duplicated token into the console session, so the
                // helper starts there. Session 0, where the service lives, has
                // no interactive desktop.
                if (!SetTokenInformation(primaryToken, TOKEN_INFORMATION_CLASS.TokenSessionId, ref sessionId, sizeof(uint)))
                {
                    return false;
                }

                // The console session's environment (USERPROFILE, APPDATA,
                // TEMP...). If it can't be built, the helper still starts, the
                // same way UserSessionProcessLauncher launches Session.
                if (!CreateEnvironmentBlock(out var environment, primaryToken, false))
                {
                    environment = IntPtr.Zero;
                }

                try
                {
                    var startupInfo = new STARTUPINFO
                    {
                        cb = Marshal.SizeOf<STARTUPINFO>(),
                        // The interactive window station and desktop, so the
                        // helper can reach (and switch to) the secure UAC desktop.
                        lpDesktop = "winsta0\\default",
                    };

                    // applicationName is the helper's path; no arguments, so the
                    // command line is null. CREATE_NO_WINDOW keeps the SYSTEM
                    // helper from flashing a console window on the student's screen.
                    var created = CreateProcessAsUser(
                        primaryToken, _deskExecutablePath, null, IntPtr.Zero, IntPtr.Zero, false,
                        CREATE_NO_WINDOW | (environment == IntPtr.Zero ? 0 : CREATE_UNICODE_ENVIRONMENT), environment,
                        Path.GetDirectoryName(_deskExecutablePath), ref startupInfo, out var processInfo);

                    if (!created)
                    {
                        return false;
                    }

                    processId = processInfo.dwProcessId;
                    CloseHandle(processInfo.hThread);
                    CloseHandle(processInfo.hProcess);
                    return true;
                }
                finally
                {
                    if (environment != IntPtr.Zero)
                    {
                        DestroyEnvironmentBlock(environment);
                    }
                }
            }
            finally
            {
                CloseHandle(primaryToken);
            }
        }
        finally
        {
            // The Agent's process token, opened above. (The pseudo-handle from
            // GetCurrentProcess is not a real handle and is not closed.)
            CloseHandle(token);
        }
    }

    public bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private enum SECURITY_IMPERSONATION_LEVEL { SecurityIdentification = 1 }
    private enum TOKEN_TYPE { TokenPrimary = 1 }
    private enum TOKEN_INFORMATION_CLASS { TokenSessionId = 12 }

    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint MAXIMUM_ALLOWED = 0x02000000;
    private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_QUERY = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr existingToken, uint desiredAccess, IntPtr tokenAttributes,
        SECURITY_IMPERSONATION_LEVEL impersonationLevel, TOKEN_TYPE tokenType, out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetTokenInformation(
        IntPtr token, TOKEN_INFORMATION_CLASS tokenInformationClass, ref uint tokenInformation, int tokenInformationLength);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr token, string? applicationName, StringBuilder? commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment,
        string? currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInfo);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);
}
