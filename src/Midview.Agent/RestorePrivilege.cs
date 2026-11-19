using System.Runtime.InteropServices;

namespace Midview.Agent;

// The uninstall cleanup runs as the administrator who uninstalls, not as
// SYSTEM. When that administrator is also the student whose Desktop
// "Travar a Área de Trabalho" locked (a lab whose students log on to an
// administrator account), the lock's own Deny keeps them from changing the
// Desktop's permissions, so it could never be given back. An administrator
// holds SeRestorePrivilege (off until asked for): with it on, a folder
// opened with backup semantics (ProtectedStudentFolders.OpenAsItself) may
// have its permissions written whatever they say, as the service may.
public static class RestorePrivilege
{
    // True when it is on; false when this account does not hold it (not an
    // administrator), which only means the Denies of its own Desktop stay.
    public static bool TryEnable()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
        {
            return false;
        }
        try
        {
            if (!LookupPrivilegeValue(null, "SeRestorePrivilege", out var luid))
            {
                return false;
            }
            var privileges = new TokenPrivileges { PrivilegeCount = 1, Luid = luid, Attributes = SePrivilegeEnabled };
            // AdjustTokenPrivileges succeeds without the privilege, and says
            // so only through the last error (ERROR_NOT_ALL_ASSIGNED).
            return AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero)
                && Marshal.GetLastWin32Error() == 0;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private const uint TokenAdjustPrivileges = 0x20;
    private const uint TokenQuery = 0x8;
    private const uint SePrivilegeEnabled = 0x2;

    // TOKEN_PRIVILEGES with one LUID_AND_ATTRIBUTES: the LUID is two DWORDs,
    // so it sits at offset 4, not 8.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public long Luid;
        public uint Attributes;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LookupPrivilegeValueW")]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr token, bool disableAll, ref TokenPrivileges newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
