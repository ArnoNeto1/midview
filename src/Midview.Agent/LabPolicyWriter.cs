using Microsoft.Win32;
using Midview.Protocol;

namespace Midview.Agent;

// Writes, removes and reads back the registry policies behind the
// lab-configuration panel (docs/superpowers/specs/
// 2026-10-08-lab-configuration-design.md). Chrome reads these machine-wide
// policies straight from the registry; Windows reads the wallpaper lock.
//
// The base key is injected so tests point it at a throwaway subtree instead
// of the machine's real policy keys: in production it is HKLM\SOFTWARE, and
// every path below is relative to it. Blocking Edge is not here -- it goes
// through the program-blocking enforcer.
public sealed class LabPolicyWriter : IDisposable
{
    private const string ChromePath = @"Policies\Google\Chrome";
    private const string ClearOnExitPath = ChromePath + @"\ClearBrowsingDataOnExitList";
    private const string UrlBlocklistPath = ChromePath + @"\URLBlocklist";
    private const string ExtensionBlocklistPath = ChromePath + @"\ExtensionInstallBlocklist";
    private const string ExtensionForcelistPath = ChromePath + @"\ExtensionInstallForcelist";
    private const string ExtensionSettingsPath = ChromePath + @"\ExtensionSettings";
    private const string RestoreUrlsPath = ChromePath + @"\RestoreOnStartupURLs";
    private const string WallpaperPath = @"Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop";
    private const string ExplorerPath = @"Policies\Microsoft\Windows\Explorer";

    private const string Homepage = "https://www.google.com.br";
    private const string AdblockExtensionId = "cfhdojbkjhnklbpkdaibdccddilifddb";
    private const string AdblockUpdateUrl = "https://clients2.google.com/service/update2/crx";
    private const string AdblockForcelistEntry = AdblockExtensionId + ";" + AdblockUpdateUrl;
    // So the ad blocker is on in incognito/guest windows too, not just normal ones.
    private const string AdblockSettings =
        "{\"installation_mode\":\"force_installed\",\"update_url\":\"" + AdblockUpdateUrl + "\",\"allowed_incognito\":true}";

    // What the student can't reach while blindagem is on: the settings,
    // personalization and theme pages.
    private static readonly string[] BlockedUrls =
    {
        "chrome://profile-picker", "chrome://settings/manageProfile", "chrome://settings/appearance",
        "chrome://settings/people", "chrome://settings/syncSetup", "chrome://settings",
        "chrome://customize-chrome-side-panel.top-chrome", "chrome-untrusted://customize-chrome-side-panel.top-chrome",
        "chrome://customize-chrome-side-panel", "chrome-untrusted://customize-chrome-side-panel",
        "chrome://theme", "chrome://color-picker",
    };

    // What "Limpeza ao fechar" destroys when Chrome closes.
    private static readonly string[] ClearOnExitKinds =
    {
        "browsing_history", "download_history", "cookies_and_other_site_data", "password_signin", "autofill",
    };

    // The pinned-icon layout for "Travar barra de tarefas": when this file
    // is present the icon order is locked to it (LockedStartLayout); without
    // it, the lock still stops the student pinning and unpinning. The teacher
    // can drop their own TaskbarLayout.xml here with "Enviar arquivo".
    public static readonly string DefaultTaskbarLayoutPath =
        Path.Combine(Environment.ExpandEnvironmentVariables("%ProgramData%"), "Midview", "TaskbarLayout.xml");

    private readonly Lazy<RegistryKey> _base;
    private readonly string _taskbarLayoutPath;

    public LabPolicyWriter(RegistryKey baseKey, string? taskbarLayoutPath = null)
        : this(() => baseKey, taskbarLayoutPath)
    {
    }

    private LabPolicyWriter(Func<RegistryKey> baseKeyFactory, string? taskbarLayoutPath)
    {
        // Opened on first use, not now: OpenMachine runs while the Agent's
        // host is being built, and a registry failure there must not keep the
        // service from starting (the first real open then happens inside the
        // reconcile's own try/catch, or a command handler's).
        _base = new Lazy<RegistryKey>(baseKeyFactory);
        _taskbarLayoutPath = taskbarLayoutPath ?? DefaultTaskbarLayoutPath;
    }

    // HKLM\SOFTWARE, writable (opened lazily). Needs the Agent's SYSTEM rights.
    public static LabPolicyWriter OpenMachine() =>
        new(() => Registry.LocalMachine.CreateSubKey("SOFTWARE", writable: true), null);

    // Makes the registry match config: each lockdown that is on is written,
    // each that is off has its keys removed. Declarative, so it is also the
    // reconcile at boot. Edge is not here.
    public void Apply(LabConfig config)
    {
        Set(config.CleanOnExit, ApplyCleanOnExit, RemoveCleanOnExit);
        Set(config.Lockdown, ApplyLockdown, RemoveLockdown);
        Set(config.BlockDownloads, ApplyBlockDownloads, RemoveBlockDownloads);
        Set(config.ForceGuest, ApplyForceGuest, RemoveForceGuest);
        Set(config.Adblock, ApplyAdblock, RemoveAdblock);
        Set(config.LockTaskbar, ApplyLockTaskbar, RemoveLockTaskbar);
    }

    // Everything back to the factory default: the whole Chrome policy subtree,
    // the wallpaper lock and the taskbar lock, so it also clears what an older
    // hand-run script left under these machine-wide keys.
    public void ResetAll()
    {
        _base.Value.DeleteSubKeyTree(ChromePath, throwOnMissingSubKey: false);
        DeleteValue(WallpaperPath, "NoChangingWallPaper");
        RemoveLockTaskbar();
    }

    private static void Set(bool on, Action apply, Action remove)
    {
        if (on)
        {
            apply();
        }
        else
        {
            remove();
        }
    }

    private void ApplyCleanOnExit()
    {
        using var key = _base.Value.CreateSubKey(ClearOnExitPath, writable: true);
        for (var i = 0; i < ClearOnExitKinds.Length; i++)
        {
            key.SetValue((i + 1).ToString(), ClearOnExitKinds[i], RegistryValueKind.String);
        }
    }

    private void RemoveCleanOnExit() => _base.Value.DeleteSubKeyTree(ClearOnExitPath, throwOnMissingSubKey: false);

    private void ApplyLockdown()
    {
        using (var chrome = _base.Value.CreateSubKey(ChromePath, writable: true))
        {
            chrome.SetValue("BrowserAddPersonEnabled", 0, RegistryValueKind.DWord);
            chrome.SetValue("BrowserSignin", 0, RegistryValueKind.DWord);
            chrome.SetValue("NTPCustomBackgroundEnabled", 0, RegistryValueKind.DWord);
            chrome.SetValue("BrowserShowProfilePickerOnStartup", 0, RegistryValueKind.DWord);
            chrome.SetValue("IncognitoModeAvailability", 1, RegistryValueKind.DWord);
            chrome.SetValue("RestoreOnStartup", 4, RegistryValueKind.DWord);
            chrome.SetValue("HomepageLocation", Homepage, RegistryValueKind.String);
            chrome.SetValue("HomepageIsNewTabPage", 0, RegistryValueKind.DWord);
            chrome.SetValue("ShowHomeButton", 1, RegistryValueKind.DWord);
        }

        using (var restore = _base.Value.CreateSubKey(RestoreUrlsPath, writable: true))
        {
            restore.SetValue("1", Homepage, RegistryValueKind.String);
        }

        // Cleared first, so the key ends up with exactly these entries and
        // not any left by an older hand-run script (which used indices 100+).
        _base.Value.DeleteSubKeyTree(UrlBlocklistPath, throwOnMissingSubKey: false);
        using (var blocklist = _base.Value.CreateSubKey(UrlBlocklistPath, writable: true))
        {
            for (var i = 0; i < BlockedUrls.Length; i++)
            {
                blocklist.SetValue((i + 1).ToString(), BlockedUrls[i], RegistryValueKind.String);
            }
        }

        using (var extensions = _base.Value.CreateSubKey(ExtensionBlocklistPath, writable: true))
        {
            // "*" blocks every extension the student might add; the
            // force-installed ad blocker still goes in through the forcelist.
            extensions.SetValue("1", "*", RegistryValueKind.String);
        }

        using var wallpaper = _base.Value.CreateSubKey(WallpaperPath, writable: true);
        wallpaper.SetValue("NoChangingWallPaper", 1, RegistryValueKind.DWord);
    }

    private void RemoveLockdown()
    {
        foreach (var value in new[]
        {
            "BrowserAddPersonEnabled", "BrowserSignin", "NTPCustomBackgroundEnabled", "BrowserShowProfilePickerOnStartup",
            "IncognitoModeAvailability", "RestoreOnStartup", "HomepageLocation", "HomepageIsNewTabPage", "ShowHomeButton",
        })
        {
            DeleteValue(ChromePath, value);
        }

        _base.Value.DeleteSubKeyTree(RestoreUrlsPath, throwOnMissingSubKey: false);
        _base.Value.DeleteSubKeyTree(UrlBlocklistPath, throwOnMissingSubKey: false);
        _base.Value.DeleteSubKeyTree(ExtensionBlocklistPath, throwOnMissingSubKey: false);
        DeleteValue(WallpaperPath, "NoChangingWallPaper");
    }

    private void ApplyBlockDownloads()
    {
        using var chrome = _base.Value.CreateSubKey(ChromePath, writable: true);
        chrome.SetValue("DownloadRestriction", 3, RegistryValueKind.DWord);
    }

    private void RemoveBlockDownloads() => DeleteValue(ChromePath, "DownloadRestriction");

    private void ApplyForceGuest()
    {
        using var chrome = _base.Value.CreateSubKey(ChromePath, writable: true);
        // Both, together: enforcing guest mode does nothing if guest mode is
        // disabled, and an older hand-run script may have left
        // BrowserGuestModeEnabled=0 behind, which silently defeated the force.
        chrome.SetValue("BrowserGuestModeEnabled", 1, RegistryValueKind.DWord);
        chrome.SetValue("BrowserGuestModeEnforced", 1, RegistryValueKind.DWord);
    }

    private void RemoveForceGuest()
    {
        DeleteValue(ChromePath, "BrowserGuestModeEnforced");
        DeleteValue(ChromePath, "BrowserGuestModeEnabled");
    }

    private void ApplyAdblock()
    {
        using (var forcelist = _base.Value.CreateSubKey(ExtensionForcelistPath, writable: true))
        {
            forcelist.SetValue("1", AdblockForcelistEntry, RegistryValueKind.String);
        }
        using var settings = _base.Value.CreateSubKey(ExtensionSettingsPath, writable: true);
        settings.SetValue(AdblockExtensionId, AdblockSettings, RegistryValueKind.String);
    }

    private void RemoveAdblock()
    {
        _base.Value.DeleteSubKeyTree(ExtensionForcelistPath, throwOnMissingSubKey: false);
        // Only this feature writes the ad blocker's entry; the key is ours.
        _base.Value.DeleteSubKeyTree(ExtensionSettingsPath, throwOnMissingSubKey: false);
    }

    private void ApplyLockTaskbar()
    {
        using var explorer = _base.Value.CreateSubKey(ExplorerPath, writable: true);
        // Stops the student pinning or unpinning anything.
        explorer.SetValue("NoPinningToTaskbar", 1, RegistryValueKind.DWord);

        // With a layout file present, also lock the icon order to it; without
        // one, don't set LockedStartLayout (on its own it can blank the Start
        // layout), so the lock is just the pin/unpin block above.
        if (File.Exists(_taskbarLayoutPath))
        {
            explorer.SetValue("LockedStartLayout", 1, RegistryValueKind.DWord);
            explorer.SetValue("StartLayoutFile", _taskbarLayoutPath, RegistryValueKind.String);
        }
        else
        {
            explorer.DeleteValue("LockedStartLayout", throwOnMissingValue: false);
            explorer.DeleteValue("StartLayoutFile", throwOnMissingValue: false);
        }
    }

    private void RemoveLockTaskbar()
    {
        foreach (var value in new[] { "NoPinningToTaskbar", "LockedStartLayout", "StartLayoutFile" })
        {
            DeleteValue(ExplorerPath, value);
        }
    }

    private void DeleteValue(string subPath, string name)
    {
        using var key = _base.Value.OpenSubKey(subPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public void Dispose()
    {
        if (_base.IsValueCreated)
        {
            _base.Value.Dispose();
        }
    }
}
