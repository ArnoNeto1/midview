using System.Text;
using Microsoft.Win32;
using Midview.Agent;
using Xunit;

// Runs against a throwaway HKCU subtree laid out like the real Uninstall
// keys, with a fake command runner, so no real program is listed or removed.
public sealed class ProgramUninstallerTests : IDisposable
{
    private const string UninstallPath = @"Microsoft\Windows\CurrentVersion\Uninstall";

    private readonly string _rootPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";
    private readonly string _userRootPath = $@"Software\MidviewTestUser\{Guid.NewGuid():N}";
    private readonly RegistryKey _root;
    private readonly List<string> _commands = new();
    private readonly List<string> _studentCommands = new();
    private int? _exitCode = 0;

    public ProgramUninstallerTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_rootPath, writable: true);
    }

    public void Dispose()
    {
        _root.Dispose();
        try { Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(_userRootPath, throwOnMissingSubKey: false); } catch { }
    }

    private void AddProgram(string keyName, Action<RegistryKey> fill)
    {
        using var key = _root.CreateSubKey($@"{UninstallPath}\{keyName}", writable: true);
        fill(key);
    }

    // Stands in for an entry under the logged-on student's own HKCU, as
    // opposed to the machine-wide hive AddProgram writes to.
    private void AddUserProgram(string keyName, Action<RegistryKey> fill)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"{_userRootPath}\{UninstallPath}\{keyName}", writable: true);
        fill(key);
    }

    private ProgramUninstaller Uninstaller() =>
        new(Registry.CurrentUser.CreateSubKey(_rootPath, writable: true), command => { _commands.Add(command); return _exitCode; });

    // Same, but with a student "logged on": their SOFTWARE hive is the
    // throwaway _userRootPath subtree instead of the real HKEY_USERS\<SID>.
    private ProgramUninstaller UninstallerWithLoggedOnStudent() =>
        new(Registry.CurrentUser.CreateSubKey(_rootPath, writable: true),
            command => { _commands.Add(command); return _exitCode; },
            () => Registry.CurrentUser.OpenSubKey(_userRootPath, writable: false));

    // Same, but with canned "Name|PackageFullName" lines standing in for
    // Get-AppxPackage's real output.
    private ProgramUninstaller UninstallerWithAppxPackages(params string[] lines) =>
        new(Registry.CurrentUser.CreateSubKey(_rootPath, writable: true),
            command => { _commands.Add(command); return _exitCode; },
            listAppxPackages: () => lines);

    // A student is "logged on", and the two runners are kept apart: machine-wide
    // programs go to the SYSTEM runner (_commands), the student's own go to the
    // runner that stands in for running in the student's session
    // (_studentCommands).
    private ProgramUninstaller UninstallerWithSeparateRunners() =>
        new(Registry.CurrentUser.CreateSubKey(_rootPath, writable: true),
            command => { _commands.Add(command); return _exitCode; },
            () => Registry.CurrentUser.OpenSubKey(_userRootPath, writable: false),
            null,
            command => { _studentCommands.Add(command); return _exitCode; });

    // Decodes the script passed to powershell.exe's -EncodedCommand in a
    // captured command line, so assertions read the real script instead of a
    // hardcoded Base64 blob that would break on every harmless wording change.
    private static string DecodeEncodedCommand(string commandLine)
    {
        const string marker = "-EncodedCommand ";
        var start = commandLine.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        return Encoding.Unicode.GetString(Convert.FromBase64String(commandLine[start..].Trim()));
    }

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
    public void ListInstalled_AlsoIncludesTheLoggedOnStudentsOwnPrograms()
    {
        AddProgram("MachineApp", k => { k.SetValue("DisplayName", "Machine App"); k.SetValue("QuietUninstallString", "un.exe /S"); });
        AddUserProgram("UserApp", k => { k.SetValue("DisplayName", "User App"); k.SetValue("QuietUninstallString", "un2.exe /S"); });

        var listed = UninstallerWithLoggedOnStudent().ListInstalled();

        Assert.Equal(new[] { "Machine App", "User App" }, listed);
    }

    [Fact]
    public void ListInstalled_NoLoggedOnStudent_OnlyUsesTheMachineHive()
    {
        AddProgram("MachineApp", k => { k.SetValue("DisplayName", "Machine App"); k.SetValue("QuietUninstallString", "un.exe /S"); });

        // Uninstaller() (no third argument) stands in for nobody at the
        // console: the user-hive factory defaults to always null.
        var listed = Uninstaller().ListInstalled();

        Assert.Equal(new[] { "Machine App" }, listed);
    }

    [Fact]
    public void Uninstall_StudentsOwnProgram_RunsItToo()
    {
        AddUserProgram("UserApp", k => { k.SetValue("DisplayName", "User App"); k.SetValue("QuietUninstallString", "un2.exe /S"); });

        var (ok, detail) = UninstallerWithLoggedOnStudent().UninstallMany(new[] { "User App" });

        Assert.True(ok);
        Assert.Contains("1 desinstalado", detail);
        Assert.Equal(new[] { "un2.exe /S" }, _commands);
    }

    [Fact]
    public void ListInstalled_IncludesAppxPackagesAlongsideRegistryPrograms()
    {
        AddProgram("MachineApp", k => { k.SetValue("DisplayName", "Machine App"); k.SetValue("QuietUninstallString", "un.exe /S"); });

        var listed = UninstallerWithAppxPackages("Store App|Vendor.StoreApp_1.0.0.0_x64__abc123").ListInstalled();

        Assert.Equal(new[] { "Machine App", "Store App" }, listed);
    }

    [Fact]
    public void ListInstalled_SkipsMalformedAppxLines()
    {
        AddProgram("MachineApp", k => { k.SetValue("DisplayName", "Machine App"); k.SetValue("QuietUninstallString", "un.exe /S"); });

        var listed = UninstallerWithAppxPackages("no separator here", "Trailing Pipe With Nothing After|").ListInstalled();

        Assert.Equal(new[] { "Machine App" }, listed);
    }

    [Fact]
    public void Uninstall_AppxPackage_RunsRemoveAppxPackageAndProvisionedPackageCommand()
    {
        var (ok, detail) = UninstallerWithAppxPackages("Store App|Vendor.StoreApp_1.0.0.0_x64__abc123")
            .UninstallMany(new[] { "Store App" });

        Assert.True(ok);
        Assert.Contains("1 desinstalado", detail);
        var command = Assert.Single(_commands);
        Assert.StartsWith("powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand ", command);
        var script = DecodeEncodedCommand(command);
        Assert.Contains("Remove-AppxPackage -Package 'Vendor.StoreApp_1.0.0.0_x64__abc123' -AllUsers", script);
        Assert.Contains("Remove-AppxProvisionedPackage", script);
    }

    [Fact]
    public void Uninstall_AppxPackageNameWithSingleQuote_EscapesItInTheScript()
    {
        // Synthetic -- a real PackageFullName never contains a quote -- just
        // to prove the escaping actually runs rather than assuming it does.
        var (ok, _) = UninstallerWithAppxPackages("Odd App|Vendor.Odd'Name_1.0.0.0_x64__abc123")
            .UninstallMany(new[] { "Odd App" });

        Assert.True(ok);
        var script = DecodeEncodedCommand(Assert.Single(_commands));
        Assert.Contains("'Vendor.Odd''Name_1.0.0.0_x64__abc123'", script);
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
    public void Uninstall_AnotherVersionHere_RemovesIt()
    {
        // Picked from a machine with 112, this one has 113.
        AddProgram("Opera GX 113", k => { k.SetValue("DisplayName", "Opera GX Stable 113.0.5230.47"); k.SetValue("QuietUninstallString", "opera113.exe /S"); });

        var (ok, detail) = Uninstaller().UninstallMany(new[] { "Opera GX Stable 112.0.5197.60" });

        Assert.True(ok);
        Assert.Contains("1 desinstalado", detail);
        Assert.Equal(new[] { "opera113.exe /S" }, _commands);
    }

    [Fact]
    public void Uninstall_ExactNameHere_LeavesTheOtherVersionsAlone()
    {
        // Two versions side by side, one of them the one picked: only that one goes.
        AddProgram("Python 3.11", k => { k.SetValue("DisplayName", "Python 3.11.4 (64-bit)"); k.SetValue("QuietUninstallString", "py311.exe /quiet"); });
        AddProgram("Python 3.12", k => { k.SetValue("DisplayName", "Python 3.12.1 (64-bit)"); k.SetValue("QuietUninstallString", "py312.exe /quiet"); });

        Uninstaller().UninstallMany(new[] { "Python 3.11.4 (64-bit)" });

        Assert.Equal(new[] { "py311.exe /quiet" }, _commands);
    }

    [Fact]
    public void Uninstall_NumberWithoutADot_MustMatchExactly()
    {
        // "Update 431" isn't a version number to ignore: a different program
        // as far as matching goes.
        AddProgram("Java 8", k => { k.SetValue("DisplayName", "Java 8 Update 441"); k.SetValue("QuietUninstallString", "java.exe /s"); });

        var (ok, detail) = Uninstaller().UninstallMany(new[] { "Java 8 Update 431" });

        Assert.True(ok);
        Assert.Contains("1 não instalado", detail);
        Assert.Empty(_commands);
    }

    [Fact]
    public void UninstallStudentsOwn_RemovesOnlyTheStudentsCopy_InTheirSession()
    {
        AddProgram("MachineApp", k => { k.SetValue("DisplayName", "Shared App"); k.SetValue("QuietUninstallString", "machine.exe /S"); });
        AddUserProgram("UserApp", k => { k.SetValue("DisplayName", "Shared App"); k.SetValue("QuietUninstallString", "user.exe /S"); });
        AddUserProgram("Discord", k => { k.SetValue("DisplayName", "Discord"); k.SetValue("QuietUninstallString", "Update.exe --uninstall -s"); });

        var removed = UninstallerWithSeparateRunners().UninstallStudentsOwn(new[] { "Shared App", "Discord", "Not Here" });

        Assert.Equal(new[] { "Shared App", "Discord" }, removed);
        Assert.Equal(new[] { "user.exe /S", "Update.exe --uninstall -s" }, _studentCommands);
        Assert.Empty(_commands); // the machine-wide copy was the teacher's request's to remove
    }

    [Fact]
    public void UninstallStudentsOwn_NobodyLoggedOn_RemovesNothing()
    {
        AddProgram("MachineApp", k => { k.SetValue("DisplayName", "Machine App"); k.SetValue("QuietUninstallString", "machine.exe /S"); });

        var removed = Uninstaller().UninstallStudentsOwn(new[] { "Machine App" });

        Assert.Empty(removed);
        Assert.Empty(_commands);
    }

    [Fact]
    public void Uninstall_NoUninstallStringAtAll_IsNotRunAndIsAFailure()
    {
        // No QuietUninstallString, not an MSI, and no plain UninstallString
        // either -- genuinely nothing to try.
        AddProgram("AppNoSilent", k => { k.SetValue("DisplayName", "App No Silent"); });

        var (ok, detail) = Uninstaller().UninstallMany(new[] { "App No Silent" });

        // The teacher asked for this program to go; it's still there, so this
        // must not look like success even though nothing crashed.
        Assert.False(ok);
        Assert.Contains("sem desinstalação silenciosa", detail);
        Assert.Empty(_commands);
    }

    [Fact]
    public void Uninstall_QuotedButStaleUninstallString_IsStillAttemptedAndFailsHonestly()
    {
        // Quoted, so the executable is trusted without checking it exists
        // (a stale remnant after a manual uninstall is exactly the sort of
        // thing this is trying to clean up) -- it's attempted and, since it
        // can't actually start, reported as a real failure rather than a
        // silent no-op.
        AddProgram("AppGone", k => { k.SetValue("DisplayName", "App Gone"); k.SetValue("UninstallString", "\"C:\\nowhere\\un.exe\""); });
        _exitCode = null; // the fake runner stands in for "couldn't start"

        var (ok, _) = Uninstaller().UninstallMany(new[] { "App Gone" });

        Assert.False(ok);
        Assert.Single(_commands);
    }

    [Fact]
    public void Uninstall_NoQuietString_FallsBackToGenericSilentSwitches()
    {
        // A plain, quoted UninstallString with no QuietUninstallString and no
        // MSI flag -- the common shape for NSIS/Inno Setup installers that
        // don't publish their own quiet switch.
        AddProgram("AppPlain", k => { k.SetValue("DisplayName", "App Plain"); k.SetValue("UninstallString", "\"C:\\b\\un.exe\""); });

        var (ok, detail) = Uninstaller().UninstallMany(new[] { "App Plain" });

        Assert.True(ok);
        Assert.Contains("1 desinstalado", detail);
        Assert.Equal(new[] { "\"C:\\b\\un.exe\" /S /VERYSILENT /SUPPRESSMSGBOXES /NORESTART" }, _commands);
    }

    [Fact]
    public void Uninstall_UninstallStringWithExistingArguments_RunsItVerbatimWithoutGenericSwitches()
    {
        // Mirrors Microsoft's own installer shape for things like Copilot:
        // "...\setup.exe" --uninstall --channel=stable --system-level. Those
        // arguments ARE the uninstall invocation, not stylistic noise -- a
        // generic /S tacked on (or instead of them) would silently do
        // nothing useful rather than uninstall anything.
        AddProgram("AppWithArgs", k =>
        {
            k.SetValue("DisplayName", "App With Args");
            k.SetValue("UninstallString", "\"C:\\b\\setup.exe\" --uninstall --channel=stable --system-level");
        });

        var (ok, _) = Uninstaller().UninstallMany(new[] { "App With Args" });

        Assert.True(ok);
        Assert.Equal(new[] { "\"C:\\b\\setup.exe\" --uninstall --channel=stable --system-level" }, _commands);
    }

    [Fact]
    public void Uninstall_UnquotedUninstallStringWithSpaces_IsResolvedByCheckingTheRealFile()
    {
        // Mirrors a real registry entry seen in the field: an unquoted path
        // containing a space ("C:\Program Files\App\uninstall.exe"), no
        // QuietUninstallString, no MSI flag. Resolving it needs a real file
        // on disk, exactly like Windows' own ambiguous-unquoted-path rule.
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Midview Fallback Test " + Guid.NewGuid().ToString("N")));
        var exePath = Path.Combine(directory.FullName, "uninstall.exe");
        File.WriteAllBytes(exePath, Array.Empty<byte>());
        try
        {
            AddProgram("AppUnquoted", k => { k.SetValue("DisplayName", "App Unquoted"); k.SetValue("UninstallString", exePath); });

            var (ok, _) = Uninstaller().UninstallMany(new[] { "App Unquoted" });

            Assert.True(ok);
            Assert.Equal(new[] { $"\"{exePath}\" /S /VERYSILENT /SUPPRESSMSGBOXES /NORESTART" }, _commands);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
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

    [Fact]
    public void Uninstall_OfficeClickToRun_AddsSilentSwitchesSoItRunsWithoutAWindow()
    {
        // Office Click-to-Run publishes its uninstall as an Add/Remove Programs
        // (scenariosubtype=ARP) call of OfficeClickToRun.exe, with no
        // DisplayLevel: run as published it tries to show a progress window,
        // which never appears in the Agent's session 0, so the Office stays put.
        AddProgram("HomeStudent2019Retail - pt-br", k =>
        {
            k.SetValue("DisplayName", "Microsoft Office Home and Student 2019 - pt-br");
            k.SetValue("UninstallString",
                "\"C:\\Program Files\\Common Files\\Microsoft Shared\\ClickToRun\\OfficeClickToRun.exe\" " +
                "scenario=install scenariosubtype=ARP sourcetype=None " +
                "productstoremove=HomeStudent2019Retail.16_pt-br_x-none culture=pt-br version.16=16.0");
        });

        Uninstaller().UninstallMany(new[] { "Microsoft Office Home and Student 2019 - pt-br" });

        var command = Assert.Single(_commands);
        Assert.Contains("OfficeClickToRun.exe", command);
        Assert.Contains("scenariosubtype=ARP", command);       // the real invocation is kept
        Assert.Contains("DisplayLevel=False", command);         // ...and made silent
        Assert.Contains("forceappshutdown=True", command);      // ...closing apps nobody can close in session 0
    }

    [Fact]
    public void Uninstall_OfficeClickToRunThatIsAlreadySilent_IsNotGivenASecondDisplayLevel()
    {
        // A published string that already says DisplayLevel is left to say it
        // once, not contradicted by a second one tacked on the end.
        AddProgram("Office", k =>
        {
            k.SetValue("DisplayName", "Office Already Silent");
            k.SetValue("UninstallString",
                "\"C:\\x\\OfficeClickToRun.exe\" scenario=install scenariosubtype=ARP DisplayLevel=False productstoremove=Foo");
        });

        Uninstaller().UninstallMany(new[] { "Office Already Silent" });

        var command = Assert.Single(_commands);
        Assert.Equal(1, command.Split("DisplayLevel=").Length - 1);
    }

    [Fact]
    public void Uninstall_PerUserProgram_RunsInTheStudentSessionNotAsSystem()
    {
        // A program in the student's own hive (OneDrive is the usual one) is
        // installed "just for me": its uninstaller only removes it for whoever
        // runs it, so running it as SYSTEM would leave the student's copy
        // behind. It has to run in the student's session.
        AddUserProgram("UserApp", k => { k.SetValue("DisplayName", "User App"); k.SetValue("QuietUninstallString", "un2.exe /S"); });

        var (ok, _) = UninstallerWithSeparateRunners().UninstallMany(new[] { "User App" });

        Assert.True(ok);
        Assert.Equal(new[] { "un2.exe /S" }, _studentCommands);
        Assert.Empty(_commands);
    }

    [Fact]
    public void Uninstall_MachineWideProgram_RunsAsSystemNotInTheStudentSession()
    {
        AddProgram("MachineApp", k => { k.SetValue("DisplayName", "Machine App"); k.SetValue("QuietUninstallString", "un.exe /S"); });

        var (ok, _) = UninstallerWithSeparateRunners().UninstallMany(new[] { "Machine App" });

        Assert.True(ok);
        Assert.Equal(new[] { "un.exe /S" }, _commands);
        Assert.Empty(_studentCommands);
    }

    [Fact]
    public void Uninstall_Office2007SetupController_WritesASilentConfigAndRunsSetup()
    {
        // Office 2007 isn't Click-to-Run: its ARP entry runs the Office Setup
        // Controller setup.exe, which has no DisplayLevel switch. Silent removal
        // needs a config.xml with Display Level="none"; the command writes that
        // file, runs setup.exe /config, then cleans it up.
        AddProgram("ENTERPRISE", k =>
        {
            k.SetValue("DisplayName", "Microsoft Office Enterprise 2007");
            k.SetValue("UninstallString",
                "\"C:\\Program Files (x86)\\Common Files\\Microsoft Shared\\OFFICE12\\Office Setup Controller\\setup.exe\" " +
                "/uninstall ENTERPRISE /dll OSETUP.DLL");
        });

        Uninstaller().UninstallMany(new[] { "Microsoft Office Enterprise 2007" });

        var command = Assert.Single(_commands);
        Assert.StartsWith("powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand ", command);
        var script = DecodeEncodedCommand(command);
        Assert.Contains("Display Level=\"none\"", script);
        Assert.Contains("/uninstall ENTERPRISE", script);
        Assert.Contains("/config", script);
        Assert.Contains("Product=\"ENTERPRISE\"", script);
        Assert.Contains("Office Setup Controller\\setup.exe", script);
    }

    [Fact]
    public void Uninstall_Office2007_WaitsForSetupAndKeepsItsConfigUntilItEnds()
    {
        // Office 2007's setup.exe is a window program, and PowerShell's & doesn't
        // wait for one: the command used to end at once with no exit code
        // ("falhou") and delete the config while setup.exe was starting, so
        // setup.exe then waited in session 0 on a question nobody could see. The
        // command is run here the way the Agent runs it, against a stand-in
        // setup.exe (Midview.Agent.Tests.FakeSetup) that takes 2 s and exits 3010.
        var setup = Path.Combine(AppContext.BaseDirectory, "setup.exe");
        AddProgram("ENTERPRISE", k =>
        {
            k.SetValue("DisplayName", "Microsoft Office Enterprise 2007");
            k.SetValue("UninstallString", $"\"{setup}\" /uninstall ENTERPRISE /dll OSETUP.DLL");
        });
        Uninstaller().UninstallMany(new[] { "Microsoft Office Enterprise 2007" });
        var report = Path.Combine(Path.GetTempPath(), $"midview-fake-setup-{Guid.NewGuid():N}.txt");

        try
        {
            var code = RunLikeTheAgent(Assert.Single(_commands), new()
            {
                ["MIDVIEW_FAKE_SETUP_REPORT"] = report,
                ["MIDVIEW_FAKE_SETUP_EXIT"] = "3010",
            });

            Assert.Equal(3010, code);
            var seen = File.ReadAllLines(report);
            Assert.StartsWith("/uninstall ENTERPRISE /dll OSETUP.DLL /config ", seen[0]);
            Assert.Contains("Display Level=\"none\"", seen[1]);
            Assert.Equal("config still there", seen[2]);
        }
        finally
        {
            File.Delete(report);
        }
    }

    // Runs a command line as ProgramUninstaller.RunCommand does: through
    // cmd.exe /s /c, with no window, waiting for cmd.exe to end.
    private static int RunLikeTheAgent(string commandLine, Dictionary<string, string> environment)
    {
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/s /c \"" + commandLine + "\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }
        using var process = System.Diagnostics.Process.Start(start)!;
        Assert.True(process.WaitForExit(60_000), "The command didn't end within a minute.");
        return process.ExitCode;
    }

    [Fact]
    public void Uninstall_Office2007_SupersedesTheSameNamedMsiSibling()
    {
        // Office 2007 registers two ARP entries with the same display name: the
        // Setup Controller orchestrator and the product MSI. The orchestrator
        // removes the whole suite, so it must run alone -- the MSI sibling would
        // otherwise run too and report a failure once the suite is already gone.
        AddProgram("ENTERPRISE", k =>
        {
            k.SetValue("DisplayName", "Microsoft Office Enterprise 2007");
            k.SetValue("UninstallString",
                "\"C:\\x\\OFFICE12\\Office Setup Controller\\setup.exe\" /uninstall ENTERPRISE /dll OSETUP.DLL");
        });
        AddProgram("{90120000-0030-0000-0000-0000000FF1CE}", k =>
        {
            k.SetValue("DisplayName", "Microsoft Office Enterprise 2007");
            k.SetValue("WindowsInstaller", 1);
        });

        Uninstaller().UninstallMany(new[] { "Microsoft Office Enterprise 2007" });

        var command = Assert.Single(_commands);
        Assert.DoesNotContain("msiexec", command, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Uninstall_WinRar_UsesOnlyItsOwnSilentSwitchNotTheGenericBundle()
    {
        // WinRAR's uninstaller goes silent with /S alone; the generic Inno/NSIS
        // bundle makes it exit 0 without removing anything. Quoted path so the
        // test doesn't depend on the file existing on the build machine.
        AddProgram("WinRAR archiver", k =>
        {
            k.SetValue("DisplayName", "WinRAR 6.11 (64-bit)");
            k.SetValue("UninstallString", "\"C:\\Program Files\\WinRAR\\uninstall.exe\"");
        });

        Uninstaller().UninstallMany(new[] { "WinRAR 6.11 (64-bit)" });

        Assert.Equal(new[] { "\"C:\\Program Files\\WinRAR\\uninstall.exe\" /S" }, _commands);
    }
}
