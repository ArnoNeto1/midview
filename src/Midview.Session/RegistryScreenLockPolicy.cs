using Microsoft.Win32;

namespace Midview.Session;

// Disables the Ctrl+Alt+Del escapes from a locked screen by writing the
// per-user policies DisableTaskMgr (Task Manager) and NoLogoff (signing
// out); Unblock removes exactly those two values and leaves the policy keys
// otherwise untouched. HKEY_CURRENT_USER, so it needs no administrator
// rights. The base key is injected so tests point it at a throwaway subtree;
// in production it is HKEY_CURRENT_USER and both paths are relative to it.
public sealed class RegistryScreenLockPolicy : IScreenLockPolicy
{
    private const string SystemPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string ExplorerPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

    private readonly RegistryKey _base;

    public RegistryScreenLockPolicy(RegistryKey? baseKey = null) => _base = baseKey ?? Registry.CurrentUser;

    public void Block()
    {
        using (var system = _base.CreateSubKey(SystemPath, writable: true))
        {
            system.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
        }
        using var explorer = _base.CreateSubKey(ExplorerPath, writable: true);
        explorer.SetValue("NoLogoff", 1, RegistryValueKind.DWord);
    }

    public void Unblock()
    {
        DeleteValue(SystemPath, "DisableTaskMgr");
        DeleteValue(ExplorerPath, "NoLogoff");
    }

    private void DeleteValue(string subPath, string name)
    {
        using var key = _base.OpenSubKey(subPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
