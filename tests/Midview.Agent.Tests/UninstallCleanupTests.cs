using System.Text.Json;
using Midview.Agent;
using Xunit;

public class UninstallCleanupTests : IDisposable
{
    private readonly string _statePath = Path.Combine(Path.GetTempPath(), $"midview-state-{Guid.NewGuid()}.json");
    private readonly string _hostsPath = Path.Combine(Path.GetTempPath(), $"midview-hosts-{Guid.NewGuid()}.txt");
    private readonly List<string[]> _netshCalls = new();

    public void Dispose()
    {
        File.Delete(_statePath);
        File.Delete(_hostsPath);
    }

    [Fact]
    public void UndoesEverythingALessonCanLeaveBehind()
    {
        File.WriteAllText(_hostsPath, HostsFileManager.Reconcile("127.0.0.1 localhost\n", new[] { "youtube.com" }));
        new SiteBlockState { InternetBlocked = true, BlockedDomains = { "youtube.com" } }.Save(_statePath);
        var firewall = new FakeFirewallController();
        var devices = new FakeInputDeviceController();

        var failures = UninstallCleanup.Run(_statePath, _hostsPath, firewall, devices, _netshCalls.Add);

        Assert.Empty(failures);
        Assert.Equal(1, devices.EnableCalls);
        Assert.Equal("127.0.0.1 localhost\n", File.ReadAllText(_hostsPath));
        Assert.Equal(1, firewall.ApplyUnblockCalls);
        Assert.Equal(0, firewall.ApplyBlockCalls);
        Assert.Equal(
            new[] { "name=MidviewAgentInbound", "name=MidviewSessionInbound", "name=MidviewAgentDiscovery" },
            _netshCalls.Select(call => call[^1]));
        Assert.All(_netshCalls, call => Assert.Equal(new[] { "advfirewall", "firewall", "delete", "rule" }, call[..4]));

        var state = SiteBlockState.Load(_statePath);
        Assert.False(state.InternetBlocked);
        Assert.Empty(state.BlockedDomains);
    }

    // The two Desktop folders become ordinary again, and the Desktop loses
    // its Deny; what the student put in them stays.
    [Fact]
    public void TheStudentsFolders_AreGivenBack()
    {
        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var desktop = Path.Combine(Path.GetTempPath(), $"midview-desktop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(desktop);
        try
        {
            ProtectedStudentFolders.Protect(desktop, me, me);
            File.WriteAllText(_hostsPath, "127.0.0.1 localhost\n");

            var failures = UninstallCleanup.Run(_statePath, _hostsPath, new FakeFirewallController(),
                new FakeInputDeviceController(), _netshCalls.Add, studentDesktops: new[] { (desktop, me) });

            Assert.Empty(failures);
            Assert.Empty(ProtectedStudentFolders.Release(desktop, me)); // nothing left to undo
            Directory.Delete(Path.Combine(desktop, Midview.Protocol.StudentFolderNames.Delivery));
        }
        finally
        {
            Directory.Delete(desktop, recursive: true);
        }
    }

    // A lesson's class folder is given back too, and Chrome and Edge stop
    // saving into it where the student's registry is loaded. Against a
    // throwaway HKCU subtree, never the real policy keys.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheClassFolder_IsGivenBack_AndTheDownloadFolderPolicyRemoved(bool hiveLoaded)
    {
        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var desktop = Path.Combine(Path.GetTempPath(), $"midview-desktop-{Guid.NewGuid():N}");
        var classFolder = Path.Combine(desktop, "Pasta da turma – 3º A");
        var rootPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";
        Directory.CreateDirectory(desktop);
        try
        {
            using var software = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(rootPath, writable: true);
            ProtectedStudentFolders.Protect(desktop, me, me);
            ProtectedStudentFolders.Protect(desktop, me, me, new[] { Path.GetFileName(classFolder) });
            DownloadFolderPolicy.Apply(software, classFolder);
            File.WriteAllText(_hostsPath, "127.0.0.1 localhost\n");

            var failures = UninstallCleanup.Run(_statePath, _hostsPath, new FakeFirewallController(),
                new FakeInputDeviceController(), _netshCalls.Add, studentDesktops: new[] { (desktop, me) },
                openUserSoftware: _ => hiveLoaded
                    ? Microsoft.Win32.Registry.CurrentUser.CreateSubKey(rootPath, writable: true)
                    : null);

            Assert.Empty(failures);
            Assert.False(new DirectoryInfo(classFolder).GetAccessControl().AreAccessRulesProtected);
            Assert.Equal(hiveLoaded ? null : classFolder, DownloadFolderPolicy.Current(software));
        }
        finally
        {
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(rootPath, throwOnMissingSubKey: false); } catch { }
            Directory.Delete(desktop, recursive: true);
        }
    }

    // Uninstalling Midview removes Midview's download folder only, never one set by someone else.
    [Fact]
    public void ADownloadFolderSetBySomeoneElse_IsLeftAlone()
    {
        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var desktop = Path.Combine(Path.GetTempPath(), $"midview-desktop-{Guid.NewGuid():N}");
        var rootPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";
        Directory.CreateDirectory(desktop);
        try
        {
            using var software = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(rootPath, writable: true);
            DownloadFolderPolicy.Apply(software, @"D:\Downloads da escola");
            File.WriteAllText(_hostsPath, "127.0.0.1 localhost\n");

            var failures = UninstallCleanup.Run(_statePath, _hostsPath, new FakeFirewallController(),
                new FakeInputDeviceController(), _netshCalls.Add, studentDesktops: new[] { (desktop, me) },
                openUserSoftware: _ => Microsoft.Win32.Registry.CurrentUser.CreateSubKey(rootPath, writable: true));

            Assert.Empty(failures);
            Assert.Equal(@"D:\Downloads da escola", DownloadFolderPolicy.Current(software));
        }
        finally
        {
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(rootPath, throwOnMissingSubKey: false); } catch { }
            ProtectedStudentFolders.Release(desktop, me);
            Directory.Delete(desktop, recursive: true);
        }
    }

    [Fact]
    public void TheProgramBlock_IsLifted_SoAReinstallDoesNotBringItBack()
    {
        var appBlockPath = Path.Combine(Path.GetTempPath(), $"midview-appblock-uninstall-{Guid.NewGuid():N}.json");
        File.WriteAllText(appBlockPath, "{}");
        File.WriteAllText(appBlockPath + ".tmp", "{}");
        File.WriteAllText(_hostsPath, "127.0.0.1 localhost\n");

        var failures = UninstallCleanup.Run(
            _statePath, _hostsPath, new FakeFirewallController(), new FakeInputDeviceController(), _netshCalls.Add, appBlockPath);

        Assert.Empty(failures);
        Assert.False(File.Exists(appBlockPath));
        Assert.False(File.Exists(appBlockPath + ".tmp"));
    }

    [Fact]
    public void ThePendingUninstalls_AreForgotten_SoAReinstallDoesNotResumeThem()
    {
        var pendingPath = Path.Combine(Path.GetTempPath(), $"midview-pending-uninstall-{Guid.NewGuid():N}.json");
        new PendingUninstalls(pendingPath).Add(new[] { "Discord" });
        File.WriteAllText(_hostsPath, "127.0.0.1 localhost\n");

        var failures = UninstallCleanup.Run(
            _statePath, _hostsPath, new FakeFirewallController(), new FakeInputDeviceController(), _netshCalls.Add,
            pendingUninstallsPath: pendingPath);

        Assert.Empty(failures);
        Assert.False(File.Exists(pendingPath));
        Assert.Empty(new PendingUninstalls(pendingPath).Current());
    }

    [Fact]
    public void AFailingStep_DoesNotStopTheOthers_AndIsReported()
    {
        File.WriteAllText(_hostsPath, "127.0.0.1 localhost\n");
        var firewall = new FakeFirewallController();

        var failures = UninstallCleanup.Run(
            _statePath, _hostsPath, firewall, new FailingDevices(),
            _ => throw new InvalidOperationException("netsh não encontrado"));

        Assert.Equal(2, failures.Count);
        Assert.Contains(failures, failure => failure.Contains("teclado e mouse"));
        Assert.Contains(failures, failure => failure.Contains("netsh não encontrado"));
        Assert.Equal(1, firewall.ApplyUnblockCalls);
        Assert.False(SiteBlockState.Load(_statePath).InternetBlocked);
    }

    [Fact]
    public void IdentityJson_HasBothThumbprints()
    {
        using var json = JsonDocument.Parse(InstallCommands.IdentityJson("AAAA", "BBBB"));

        Assert.Equal("AAAA", json.RootElement.GetProperty("agentThumbprint").GetString());
        Assert.Equal("BBBB", json.RootElement.GetProperty("sessionThumbprint").GetString());
    }

    private sealed class FailingDevices : IInputDeviceController
    {
        public bool DisableInputDevices() => false;

        public bool EnableInputDevices() => false;
    }
}
