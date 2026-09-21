using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TreezeSuite.Agent;

public sealed class UserSessionProcessLauncher : ISessionProcessLauncher
{
    private readonly string _executablePath;

    public UserSessionProcessLauncher(string executablePath)
    {
        _executablePath = executablePath;
    }

    public bool TryLaunch(out int processId)
    {
        processId = 0;
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF)
        {
            return false; // no active session, e.g. sitting at the logon screen
        }

        if (!WTSQueryUserToken(sessionId, out var userToken))
        {
            return false;
        }

        try
        {
            if (!DuplicateTokenEx(userToken, 0x02000000, IntPtr.Zero,
                    SECURITY_IMPERSONATION_LEVEL.SecurityIdentification, TOKEN_TYPE.TokenPrimary, out var primaryToken))
            {
                return false;
            }

            try
            {
                var startupInfo = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
                var created = CreateProcessAsUser(
                    primaryToken, _executablePath, null, IntPtr.Zero, IntPtr.Zero, false,
                    0, IntPtr.Zero, null, ref startupInfo, out var processInfo);

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
                CloseHandle(primaryToken);
            }
        }
        finally
        {
            CloseHandle(userToken);
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

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr existingToken, uint desiredAccess, IntPtr tokenAttributes,
        SECURITY_IMPERSONATION_LEVEL impersonationLevel, TOKEN_TYPE tokenType, out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr token, string applicationName, string? commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment,
        string? currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInfo);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
