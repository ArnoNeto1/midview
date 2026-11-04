using Microsoft.Win32;

namespace Midview.Agent;

// Hides "Switch user" on the Ctrl+Alt+Del screen by writing the HKLM policy
// HideFastUserSwitching; Unblock removes that value and leaves the key
// otherwise untouched. HKLM, so it needs the Agent's SYSTEM rights. The base
// key is injected so tests point it at a throwaway subtree; in production it
// is HKLM\SOFTWARE (OpenMachine) and the path is relative to it.
public sealed class RegistrySwitchUserPolicy : ISwitchUserPolicy
{
    private const string SystemPath = @"Microsoft\Windows\CurrentVersion\Policies\System";

    private readonly RegistryKey _base;

    public RegistrySwitchUserPolicy(RegistryKey baseKey) => _base = baseKey;

    public static RegistrySwitchUserPolicy OpenMachine() =>
        new(Registry.LocalMachine.CreateSubKey("SOFTWARE", writable: true));

    public void Block()
    {
        using var system = _base.CreateSubKey(SystemPath, writable: true);
        system.SetValue("HideFastUserSwitching", 1, RegistryValueKind.DWord);
    }

    public void Unblock()
    {
        using var key = _base.OpenSubKey(SystemPath, writable: true);
        key?.DeleteValue("HideFastUserSwitching", throwOnMissingValue: false);
    }
}
