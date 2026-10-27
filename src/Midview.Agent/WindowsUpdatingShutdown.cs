using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Midview.Agent;

// A shutdown or restart that installs the Windows updates waiting for one on
// the way, as Windows' own "Atualizar e desligar" / "Atualizar e reiniciar"
// does: only what Windows already downloaded; with nothing waiting it just
// shuts down or restarts. Warning: what Windows shows the logged-in student
// during the delay; null with no delay.
public sealed record UpdatingShutdown(bool Restart, int DelaySeconds, string? Warning);

// Asks Windows for one through InitiateShutdown, the call with a flag for
// it (shutdown.exe has no switch for it). The Agent runs as SYSTEM, which
// holds the shutdown privilege but has to turn it on first.
public static class WindowsUpdatingShutdown
{
    private const uint ShutdownForceOthers = 0x1; // SHUTDOWN_FORCE_OTHERS
    private const uint ShutdownForceSelf = 0x2; // SHUTDOWN_FORCE_SELF
    private const uint ShutdownRestart = 0x4; // SHUTDOWN_RESTART
    private const uint ShutdownPowerOff = 0x8; // SHUTDOWN_POWEROFF
    private const uint ShutdownInstallUpdates = 0x40; // SHUTDOWN_INSTALL_UPDATES

    // Planned, other (shutdown.exe's /d p:0:0): a zero reason is logged as
    // unplanned, which can slow the shutdown down.
    private const uint ReasonPlannedOther = 0x80000000; // SHTDN_REASON_FLAG_PLANNED

    // Forced like shutdown.exe's /f: open programs close without saving when
    // the time is up, so an unsaved document can't keep the machine on.
    public static uint Flags(bool restart) =>
        ShutdownForceOthers | ShutdownForceSelf | ShutdownInstallUpdates | (restart ? ShutdownRestart : ShutdownPowerOff);

    // The Windows error code: 0 when Windows took the request.
    public static int Initiate(UpdatingShutdown request)
    {
        EnableShutdownPrivilege();
        return (int)InitiateShutdown(
            null, request.DelaySeconds > 0 ? request.Warning : null, (uint)request.DelaySeconds, Flags(request.Restart), ReasonPlannedOther);
    }

    private static void EnableShutdownPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        try
        {
            if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out var luid))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            var privileges = new TokenPrivileges { PrivilegeCount = 1, Luid = luid, Attributes = SePrivilegeEnabled };
            if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero)
                || Marshal.GetLastWin32Error() != 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
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

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "InitiateShutdownW")]
    private static extern uint InitiateShutdown(string? machineName, string? message, uint gracePeriod, uint flags, uint reason);

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
