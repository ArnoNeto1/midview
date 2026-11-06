using Microsoft.Win32;

namespace Midview.Session;

// Disables the Ctrl+Alt+Del escapes from a locked screen by writing the
// per-user policies DisableTaskMgr (Task Manager) and NoLogoff (signing
// out); Unblock removes exactly those two values and leaves the policy keys
// otherwise untouched. HKEY_CURRENT_USER, so it needs no administrator
// rights -- where the account may write them: on a standard student account
// Windows leaves the Policies keys read-only, opening them to write throws,
// and then nothing is changed (the lock itself still covers the screen and
// holds the keyboard and mouse). Neither ever throws: Unblock runs as the
// Session starts, and a throw there ended the Session before it served, on
// every start (0.0.42 in the lab). The base key is injected so tests point
// it at a throwaway subtree; in production it is HKEY_CURRENT_USER and both
// paths are relative to it.
public sealed class RegistryScreenLockPolicy : IScreenLockPolicy
{
    private const string SystemPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string ExplorerPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

    private readonly RegistryKey _base;
    private readonly Action<string> _log;

    public RegistryScreenLockPolicy(RegistryKey? baseKey = null, Action<string>? log = null)
    {
        _base = baseKey ?? Registry.CurrentUser;
        _log = log ?? Console.WriteLine;
    }

    public void Block()
    {
        SetValue(SystemPath, "DisableTaskMgr");
        SetValue(ExplorerPath, "NoLogoff");
    }

    public void Unblock()
    {
        DeleteValue(SystemPath, "DisableTaskMgr");
        DeleteValue(ExplorerPath, "NoLogoff");
    }

    private void SetValue(string subPath, string name)
    {
        try
        {
            using var key = _base.CreateSubKey(subPath, writable: true);
            key.SetValue(name, 1, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (IsAccessProblem(ex))
        {
            _log($"Screen lock policy: couldn't set {name} ({ex.Message}).");
        }
    }

    // Opened to write only when the value is there, so an account whose
    // policy keys it can only read isn't even asked on every start.
    private void DeleteValue(string subPath, string name)
    {
        try
        {
            using (var readOnly = _base.OpenSubKey(subPath))
            {
                if (readOnly?.GetValue(name) is null)
                {
                    return;
                }
            }
            using var key = _base.OpenSubKey(subPath, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
        }
        catch (Exception ex) when (IsAccessProblem(ex))
        {
            _log($"Screen lock policy: couldn't remove {name} ({ex.Message}).");
        }
    }

    private static bool IsAccessProblem(Exception ex) =>
        ex is System.Security.SecurityException or UnauthorizedAccessException or IOException;
}
