using Microsoft.Win32;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

// Runs against a throwaway subtree under HKCU, never the machine's real
// policy keys, with a taskbar-layout path under the temp folder.
public sealed class LabPolicyWriterTests : IDisposable
{
    private const string ChromePath = @"Policies\Google\Chrome";
    private const string ExplorerPath = @"Policies\Microsoft\Windows\Explorer";
    private const string UrlBlocklistPath = ChromePath + @"\URLBlocklist";
    private const string WallpaperPath = @"Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop";

    private readonly string _rootPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";
    private readonly RegistryKey _root;
    private readonly string _layoutPath = Path.Combine(Path.GetTempPath(), $"midview-taskbar-{Guid.NewGuid():N}.xml");

    public LabPolicyWriterTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_rootPath, writable: true);
    }

    public void Dispose()
    {
        _root.Dispose();
        try { Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false); } catch { }
        try { File.Delete(_layoutPath); } catch { }
    }

    private LabPolicyWriter Writer() => new(Registry.CurrentUser.CreateSubKey(_rootPath, writable: true), _layoutPath);

    private object? Read(string subPath, string name)
    {
        using var key = _root.OpenSubKey(subPath);
        return key?.GetValue(name);
    }

    private bool SubKeyExists(string subPath)
    {
        using var key = _root.OpenSubKey(subPath);
        return key is not null;
    }

    [Fact]
    public void ApplyLockdown_WritesTheChromeAndWallpaperKeys()
    {
        Writer().Apply(LabConfig.Off(1) with { Lockdown = true });

        Assert.Equal(1, Read(ChromePath, "IncognitoModeAvailability"));
        Assert.Equal(0, Read(ChromePath, "BrowserSignin"));
        Assert.Equal(1, Read(WallpaperPath, "NoChangingWallPaper"));
        Assert.True(SubKeyExists(ChromePath + @"\URLBlocklist"));
    }

    [Fact]
    public void ApplyThenRemove_OneFunction_LeavesNothingOfIt()
    {
        Writer().Apply(LabConfig.Off(1) with { BlockDownloads = true });
        Assert.Equal(3, Read(ChromePath, "DownloadRestriction"));

        Writer().Apply(LabConfig.Off(2)); // declarative: downloads now off
        Assert.Null(Read(ChromePath, "DownloadRestriction"));
    }

    [Fact]
    public void Apply_IsDeclarative_OneFunctionOffDoesNotTouchAnother()
    {
        Writer().Apply(LabConfig.Off(1) with { BlockDownloads = true, ForceGuest = true });
        Writer().Apply(LabConfig.Off(2) with { ForceGuest = true }); // downloads off, guest kept

        Assert.Null(Read(ChromePath, "DownloadRestriction"));
        Assert.Equal(1, Read(ChromePath, "BrowserGuestModeEnforced"));
    }

    [Fact]
    public void ApplyAdblock_WritesTheForcelistAndIncognitoSettings()
    {
        Writer().Apply(LabConfig.Off(1) with { Adblock = true });

        Assert.Equal(
            "cfhdojbkjhnklbpkdaibdccddilifddb;https://clients2.google.com/service/update2/crx",
            Read(ChromePath + @"\ExtensionInstallForcelist", "1"));
        var settings = Read(ChromePath + @"\ExtensionSettings", "cfhdojbkjhnklbpkdaibdccddilifddb") as string;
        Assert.Contains("force_installed", settings);
        Assert.Contains("allowed_incognito", settings);
    }

    [Fact]
    public void ForceGuest_EnablesGuestMode_EvenOverAStaleDisabledLeftover()
    {
        // As an older hand-run script left it: guest mode disabled, which
        // silently defeats "enforced".
        using (var chrome = _root.CreateSubKey(ChromePath, writable: true))
        {
            chrome!.SetValue("BrowserGuestModeEnabled", 0, RegistryValueKind.DWord);
        }

        Writer().Apply(LabConfig.Off(1) with { ForceGuest = true });

        Assert.Equal(1, Read(ChromePath, "BrowserGuestModeEnforced"));
        Assert.Equal(1, Read(ChromePath, "BrowserGuestModeEnabled"));
    }

    [Fact]
    public void ForceGuest_Off_RemovesBothGuestValues()
    {
        Writer().Apply(LabConfig.Off(1) with { ForceGuest = true });
        Writer().Apply(LabConfig.Off(2));

        Assert.Null(Read(ChromePath, "BrowserGuestModeEnforced"));
        Assert.Null(Read(ChromePath, "BrowserGuestModeEnabled"));
    }

    [Fact]
    public void ApplyLockdown_ClearsStaleUrlBlocklistEntries()
    {
        // As an older hand-run script left them (indices 100+).
        using (var stale = _root.CreateSubKey(UrlBlocklistPath, writable: true))
        {
            stale!.SetValue("100", "*.facebook.com");
        }

        Writer().Apply(LabConfig.Off(1) with { Lockdown = true });

        Assert.Null(Read(UrlBlocklistPath, "100"));
        Assert.Equal("chrome://profile-picker", Read(UrlBlocklistPath, "1"));
    }

    [Fact]
    public void LockTaskbar_WithoutALayoutFile_OnlyBlocksPinning()
    {
        Writer().Apply(LabConfig.Off(1) with { LockTaskbar = true });

        Assert.Equal(1, Read(ExplorerPath, "NoPinningToTaskbar"));
        Assert.Null(Read(ExplorerPath, "LockedStartLayout"));
        Assert.Null(Read(ExplorerPath, "StartLayoutFile"));
    }

    [Fact]
    public void LockTaskbar_WithALayoutFile_AlsoLocksTheOrder()
    {
        File.WriteAllText(_layoutPath, "<LayoutModificationTemplate/>");
        Writer().Apply(LabConfig.Off(1) with { LockTaskbar = true });

        Assert.Equal(1, Read(ExplorerPath, "NoPinningToTaskbar"));
        Assert.Equal(1, Read(ExplorerPath, "LockedStartLayout"));
        Assert.Equal(_layoutPath, Read(ExplorerPath, "StartLayoutFile"));
    }

    [Fact]
    public void ResetAll_WipesEverythingItManages()
    {
        Writer().Apply(new LabConfig(1, true, true, true, true, true, false, true));
        // Something a hand-run script might have left behind, under the same subtree.
        using (var stray = _root.CreateSubKey(ChromePath, writable: true))
        {
            stray!.SetValue("SomethingOld", "x");
        }

        Writer().ResetAll();

        Assert.False(SubKeyExists(ChromePath));
        Assert.Null(Read(WallpaperPath, "NoChangingWallPaper"));
        Assert.Null(Read(ExplorerPath, "NoPinningToTaskbar"));
    }
}
