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

    // Runs a command line as the student logged on at the console -- not as
    // this SYSTEM service -- with no window, waiting a few minutes for it.
    // A program installed "just for me" (OneDrive is the usual one) has its
    // uninstaller remove only the copy belonging to whoever runs it, so run as
    // SYSTEM it would leave the student's copy behind; it has to run as the
    // student. The command goes through cmd.exe /s /c the same way the SYSTEM
    // runner does, so an uninstall string with its own quoted paths isn't
    // mangled. Returns the exit code, or null when there's nobody logged on, or
    // it couldn't start, or it didn't finish in time (then it's killed, so a
    // "silent" uninstaller still wanting a window nobody can see isn't left
    // running in the student's session).
    public static int? RunInLoggedOnStudentSession(string commandLine)
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

            var environment = IntPtr.Zero;
            try
            {
                // The student's own environment (USERPROFILE, APPDATA, TEMP...),
                // so the uninstaller resolves their per-user paths, not this
                // service's. If it can't be built, the process still starts.
                if (!CreateEnvironmentBlock(out environment, primaryToken, false))
                {
                    environment = IntPtr.Zero;
                }

                var startupInfo = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
                // applicationName null: the command line names cmd.exe, found on
                // PATH, and CreateProcessW may write to the buffer, so it's a
                // StringBuilder.
                var commandBuffer = new StringBuilder("cmd.exe /s /c \"" + commandLine + "\"");
                var created = CreateProcessAsUser(
                    primaryToken, null, commandBuffer, IntPtr.Zero, IntPtr.Zero, false,
                    CREATE_NO_WINDOW | (environment == IntPtr.Zero ? 0 : CREATE_UNICODE_ENVIRONMENT), environment,
                    null, ref startupInfo, out var processInfo);
                if (!created)
                {
                    return null;
                }

                try
                {
                    const uint timeoutMilliseconds = 5 * 60 * 1000;
                    if (WaitForSingleObject(processInfo.hProcess, timeoutMilliseconds) == WAIT_OBJECT_0
                        && GetExitCodeProcess(processInfo.hProcess, out var exitCode))
                    {
                        return unchecked((int)exitCode);
                    }

                    // Didn't finish in time (or the code couldn't be read):
                    // don't leave it running in the student's session.
                    TerminateProcess(processInfo.hProcess, 1);
                    return null;
                }
                finally
                {
                    CloseHandle(processInfo.hThread);
                    CloseHandle(processInfo.hProcess);
                }
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
            if (StudentDesktop(token, out var result) is not { Desktop: { } desktop } student)
            {
                if (result != 0)
                {
                    AgentLog.Warn($"The student's Desktop wasn't found (0x{result:X8}); their folders aren't protected.");
                }
                return; // a Desktop that may not be used was logged once (StudentDesktopCheck)
            }

            var problems = ProtectedStudentFolders.Protect(
                desktop, student.Student, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
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

    // The student logged on at the console, or null when nobody is, and their
    // Desktop where it really is: null when Windows has none for them, or it is
    // one the Agent may not work on (StudentDesktopCheck, logged once). Only
    // the Desktop work (the protected folders, the class folder, pointing the
    // browsers at it) needs the Desktop; the student is there either way, for
    // what doesn't (their own programs, the removals left pending for them).
    // The same token as for starting Session.
    public static (string? Desktop, SecurityIdentifier Student)? LoggedOnStudent()
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

    // The student of the token and their Desktop (StudentAndDesktop); null
    // only without a student. result: SHGetKnownFolderPath's, 0 for a Desktop
    // Windows found, refused or not.
    private static (string? Desktop, SecurityIdentifier Student)? StudentDesktop(IntPtr token, out int result)
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
        if (identity.User is not { } student)
        {
            return null;
        }

        var resolved = StudentAndDesktop(student, desktop, StudentDesktopCheck.ProfileImagePathOf, out var refusal);
        if (refusal is not null)
        {
            StudentDesktopCheck.ReportOnce(refusal);
        }
        return resolved;
    }

    // The student, and their Desktop as their own registry says it is (User
    // Shell Folders, which the student can change) only where it really is,
    // and only if that is in their profile or their own folder
    // (StudentDesktopCheck, Ruling 12): never another account's Desktop.
    // Desktop null when there is none (refusal null) or it is refused
    // (refusal says why); the student is kept either way.
    internal static (string? Desktop, SecurityIdentifier Student) StudentAndDesktop(
        SecurityIdentifier student, string? shellDesktop, Func<SecurityIdentifier, string?> profilePathOf, out string? refusal)
    {
        refusal = null;
        if (string.IsNullOrEmpty(shellDesktop))
        {
            return (null, student);
        }

        var verified = StudentDesktopCheck.Verify(shellDesktop, student, profilePathOf, out var why);
        if (verified is null)
        {
            refusal = why;
        }
        return (verified, student);
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
    private const uint WAIT_OBJECT_0 = 0x00000000;

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
        IntPtr token, string? applicationName, StringBuilder? commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment,
        string? currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInfo);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr handle, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr handle, uint exitCode);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);
}
