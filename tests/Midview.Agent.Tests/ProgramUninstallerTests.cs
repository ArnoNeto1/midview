using Microsoft.Win32;
using Midview.Agent;
using Xunit;

// Runs against a throwaway HKCU subtree laid out like the real Uninstall
// keys, with a fake command runner, so no real program is listed or removed.
public sealed class ProgramUninstallerTests : IDisposable
{
    private const string UninstallPath = @"Microsoft\Windows\CurrentVersion\Uninstall";

    private readonly string _rootPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";
    private readonly RegistryKey _root;
    private readonly List<string> _commands = new();
    private int? _exitCode = 0;

    public ProgramUninstallerTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_rootPath, writable: true);
    }

    public void Dispose()
    {
        _root.Dispose();
        try { Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false); } catch { }
    }

    private void AddProgram(string keyName, Action<RegistryKey> fill)
    {
        using var key = _root.CreateSubKey($@"{UninstallPath}\{keyName}", writable: true);
        fill(key);
    }

    private ProgramUninstaller Uninstaller() =>
        new(Registry.CurrentUser.CreateSubKey(_rootPath, writable: true), command => { _commands.Add(command); return _exitCode; });

    [Fact]
    public void ListInstalled_ShowsRealProgramsAndSkipsComponentsAndUpdates()
    {
        AddProgram("AppQuiet", k => { k.SetValue("DisplayName", "App Quiet"); k.SetValue("QuietUninstallString", "\"C:\\a\\un.exe\" /S"); });
        AddProgram("{11112222-3333-4444-5555-666677778888}", k => { k.SetValue("DisplayName", "App MSI"); k.SetValue("WindowsInstaller", 1); });
        AddProgram("AppNoSilent", k => { k.SetValue("DisplayName", "App No Silent"); k.SetValue("UninstallString", "\"C:\\b\\un.exe\""); });
        AddProgram("SysThing", k => { k.SetValue("DisplayName", "Sys Thing"); k.SetValue("SystemComponent", 1); });
        AddProgram("Patch", k => { k.SetValue("DisplayName", "A Patch"); k.SetValue("ParentKeyName", "AppQuiet"); });
        AddProgram("NoName", k => { k.SetValue("UninstallString", "x"); });

        var listed = Uninstaller().ListInstalled();

        Assert.Equal(new[] { "App MSI", "App No Silent", "App Quiet" }, listed); // sorted, filtered
    }

    [Fact]
    public void Uninstall_QuietString_RunsIt_AndReportsRemoved()
    {
        AddProgram("AppQuiet", k => { k.SetValue("DisplayName", "App Quiet"); k.SetValue("QuietUninstallString", "\"C:\\a\\un.exe\" /S"); });

        var (ok, detail) = Uninstaller().UninstallMany(new[] { "App Quiet" });

        Assert.True(ok);
        Assert.Contains("1 desinstalado", detail);
        Assert.Equal(new[] { "\"C:\\a\\un.exe\" /S" }, _commands);
    }

    [Fact]
    public void Uninstall_Msi_BuildsASilentMsiexec()
    {
        AddProgram("{11112222-3333-4444-5555-666677778888}", k => { k.SetValue("DisplayName", "App MSI"); k.SetValue("WindowsInstaller", 1); });

        Uninstaller().UninstallMany(new[] { "App MSI" });

        Assert.Equal(new[] { "msiexec.exe /x {11112222-3333-4444-5555-666677778888} /qn /norestart" }, _commands);
    }

    [Fact]
    public void Uninstall_NoSilentUninstall_IsNotRun()
    {
        AddProgram("AppNoSilent", k => { k.SetValue("DisplayName", "App No Silent"); k.SetValue("UninstallString", "\"C:\\b\\un.exe\""); });

        var (ok, detail) = Uninstaller().UninstallMany(new[] { "App No Silent" });

        Assert.True(ok); // nothing failed; it just wasn't removed
        Assert.Contains("sem desinstalação silenciosa", detail);
        Assert.Empty(_commands);
    }

    [Fact]
    public void Uninstall_NotInstalledHere_IsReported()
    {
        var (ok, detail) = Uninstaller().UninstallMany(new[] { "Something Else" });

        Assert.True(ok);
        Assert.Contains("não instalado", detail);
        Assert.Empty(_commands);
    }

    [Fact]
    public void Uninstall_UninstallerFails_IsAFailure()
    {
        AddProgram("AppQuiet", k => { k.SetValue("DisplayName", "App Quiet"); k.SetValue("QuietUninstallString", "un.exe /S"); });
        _exitCode = 1;

        var (ok, detail) = Uninstaller().UninstallMany(new[] { "App Quiet" });

        Assert.False(ok);
        Assert.Contains("falhou", detail);
    }

    [Fact]
    public void Uninstall_SameNameInBothHives_RunsBoth()
    {
        AddProgram("AppX", k => { k.SetValue("DisplayName", "Shared App"); k.SetValue("QuietUninstallString", "un64 /S"); });
        using (var k32 = _root.CreateSubKey(
            @"WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\AppX", writable: true))
        {
            k32.SetValue("DisplayName", "Shared App");
            k32.SetValue("QuietUninstallString", "un32 /S");
        }

        var (ok, _) = Uninstaller().UninstallMany(new[] { "Shared App" });

        Assert.True(ok);
        Assert.Equal(new[] { "un64 /S", "un32 /S" }.OrderBy(c => c), _commands.OrderBy(c => c));
    }

    [Fact]
    public void Uninstall_RebootExitCode_CountsAsRemoved()
    {
        AddProgram("AppQuiet", k => { k.SetValue("DisplayName", "App Quiet"); k.SetValue("QuietUninstallString", "un.exe /S"); });
        _exitCode = 3010;

        var (ok, detail) = Uninstaller().UninstallMany(new[] { "App Quiet" });

        Assert.True(ok);
        Assert.Contains("1 desinstalado", detail);
    }
}
