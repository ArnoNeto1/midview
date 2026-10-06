using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace Midview.Agent;

public sealed class UserSessionProcessLauncher : ISessionProcessLauncher
{
    private readonly string _executablePath;

    public UserSessionProcessLauncher(string executablePath)
    {
        _executablePath = executablePath;
    }

    public bool TryLaunch(out int processId) => Launch(null, out processId);

    // Another Session for something on the student's screen (the pairing
    // code), never the server: its arguments say what to show.
    public bool TryLaunchWithArguments(string arguments, out int processId) => Launch(arguments, out processId);

    // Whether someone is logged on at the console, so something launched
    // there would be seen.
    public static bool SomeoneLoggedOn()
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF || !WTSQueryUserToken(sessionId, out var userToken))
        {
            return false;
        }
        CloseHandle(userToken);
        return true;
    }

    private bool Launch(string? arguments, out int processId)
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

            if (arguments is null)
            {
                ProtectStudentFolders(primaryToken);
            }

            // The student's own environment (USERPROFILE, APPDATA, TEMP...),
            // not the one this service runs with. Without it Session asked
            // Windows for the student's Desktop and got nothing back, so the
            // first live run found "Arquivos do professor" being created in
            // C:\Windows\system32, Session's inherited working folder. If it
            // can't be built, Session still starts, as it did before.
            if (!CreateEnvironmentBlock(out var environment, primaryToken, false))
            {
                environment = IntPtr.Zero;
            }

            try
            {
                var startupInfo = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
                // CreateProcessW may write to the command line, so it's a buffer.
                var commandLine = arguments is null ? null : new StringBuilder($"\"{_executablePath}\" {arguments}");
                // CREATE_NO_WINDOW: Session.exe stays a console app (so running
                // it by hand for manual testing still prints its thumbprint),
                // but the auto-launched instance a student would actually see
                // gets no visible console window.
                var created = CreateProcessAsUser(
                    primaryToken, _executablePath, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    CREATE_NO_WINDOW | (environment == IntPtr.Zero ? 0 : CREATE_UNICODE_ENVIRONMENT), environment,
                    Path.GetDirectoryName(_executablePath), ref startupInfo, out var processInfo);

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
                CloseHandle(primaryToken);
            }
        }
        finally
        {
            CloseHandle(userToken);
        }
    }

    // At every start of Session, before it runs, so its two Desktop folders
    // are already there and protected (recreated if they went missing).
    // Never keeps Session from starting: what fails is logged.
    private static void ProtectStudentFolders(IntPtr token)
    {
        try
        {
            if (StudentDesktop(token, out var result) is not { } student)
            {
                AgentLog.Warn($"The student's Desktop wasn't found (0x{result:X8}); their folders aren't protected.");
                return;
            }

            var problems = ProtectedStudentFolders.Protect(
                student.Desktop, student.Student, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
            foreach (var problem in problems)
            {
                AgentLog.Warn($"A student folder couldn't be protected: {problem}");
            }
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Protecting the student's folders failed: {ex.Message}");
        }
    }

    // The student logged on at the console and their Desktop, or null when
    // nobody is or it can't be found (StudentFolderGuard, between Session's
    // starts). The same token as for starting Session.
    public static (string Desktop, SecurityIdentifier Student)? LoggedOnStudent()
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF || !WTSQueryUserToken(sessionId, out var userToken))
        {
            return null;
        }

        try
        {
            if (!DuplicateTokenEx(userToken, 0x02000000, IntPtr.Zero,
                    SECURITY_IMPERSONATION_LEVEL.SecurityIdentification, TOKEN_TYPE.TokenPrimary, out var primaryToken))
            {
                return null;
            }
            try
            {
                return StudentDesktop(primaryToken, out _);
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

    private static (string Desktop, SecurityIdentifier Student)? StudentDesktop(IntPtr token, out int result)
    {
        using var identity = new WindowsIdentity(token);
        var desktopFolderId = new Guid("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"); // FOLDERID_Desktop
        result = SHGetKnownFolderPath(ref desktopFolderId, 0, token, out var desktopPointer);
        string? desktop;
        try
        {
            desktop = result == 0 ? Marshal.PtrToStringUni(desktopPointer) : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(desktopPointer);
        }
        return identity.User is { } student && !string.IsNullOrEmpty(desktop) ? (desktop, student) : null;
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

    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

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
        IntPtr token, string applicationName, StringBuilder? commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment,
        string? currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInfo);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);
}
