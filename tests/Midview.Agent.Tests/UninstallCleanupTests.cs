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
            new[]
            {
                "name=MidviewAgentInbound", "name=MidviewSessionInbound", "name=MidviewAgentDiscovery",
                "name=MidviewDeskInbound", "name=MidviewSessionProgram",
            },
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

    // "Travar a Área de Trabalho" is undone too: the student can create,
    // rename and delete on their Desktop again.
    [Fact]
    public void ALockedDesktop_IsUnlocked()
    {
        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var desktop = Path.Combine(Path.GetTempPath(), $"midview-desktop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(desktop);
        File.WriteAllText(Path.Combine(desktop, "trabalho.txt"), "x");
        try
        {
            StudentDesktopLock.Lock(desktop, me, guardPermissions: false);
            File.WriteAllText(_hostsPath, "127.0.0.1 localhost\n");

            var failures = UninstallCleanup.Run(_statePath, _hostsPath, new FakeFirewallController(),
                new FakeInputDeviceController(), _netshCalls.Add, studentDesktops: new[] { (desktop, me) });

            Assert.Empty(failures);
            Assert.False(StudentDesktopLock.IsAnyLocked(desktop, me));
            File.Move(Path.Combine(desktop, "trabalho.txt"), Path.Combine(desktop, "renomeado.txt"));
        }
        finally
        {
            StudentDesktopLock.Unlock(desktop, me);
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

    // The Session pointed Downloads at the class folder during a lesson; uninstalling gives it back.
    [Fact]
    public void TheSaveLocations_ArePutBack()
    {
        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var profile = Path.Combine(Path.GetTempPath(), $"midview-profile-{Guid.NewGuid():N}");
        var desktop = Path.Combine(profile, "Desktop");
        var rootPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";
        Directory.CreateDirectory(desktop);
        try
        {
            using var software = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(rootPath, writable: true);
            var classFolder = Path.Combine(desktop, "Pasta da turma – 3º A");
            using (var key = software.CreateSubKey(Midview.Protocol.ClassSaveTargets.DownloadsKey, writable: true))
            {
                key.SetValue(Midview.Protocol.ClassSaveTargets.DownloadsName, classFolder, Microsoft.Win32.RegistryValueKind.ExpandString);
            }
            var statePath = WriteState(profile, new Midview.Protocol.SavedSetting(
                Midview.Protocol.ClassSaveTargets.DownloadsKey, Midview.Protocol.ClassSaveTargets.DownloadsName,
                Microsoft.Win32.RegistryValueKind.ExpandString, @"%USERPROFILE%\Downloads", classFolder));
            var before = File.ReadAllBytes(statePath);
            File.WriteAllText(_hostsPath, "127.0.0.1 localhost\n");

            var failures = UninstallCleanup.Run(_statePath, _hostsPath, new FakeFirewallController(),
                new FakeInputDeviceController(), _netshCalls.Add, studentDesktops: new[] { (desktop, me) },
                openUserSoftware: _ => Microsoft.Win32.Registry.CurrentUser.CreateSubKey(rootPath, writable: true));

            Assert.Empty(failures);
            using var read = software.OpenSubKey(Midview.Protocol.ClassSaveTargets.DownloadsKey)!;
            Assert.Equal(@"%USERPROFILE%\Downloads", read.GetValue(Midview.Protocol.ClassSaveTargets.DownloadsName, null,
                Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames));
            Assert.Equal(before, File.ReadAllBytes(statePath));
        }
        finally
        {
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(rootPath, throwOnMissingSubKey: false); } catch { }
            ProtectedStudentFolders.Release(desktop, me);
            Directory.Delete(profile, recursive: true);
        }
    }

    // The state file is the student's to edit: a setting outside the allowlist writes nothing.
    [Fact]
    public void ACraftedStateFile_WritesNothingOutsideTheAllowlist()
    {
        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var profile = Path.Combine(Path.GetTempPath(), $"midview-profile-{Guid.NewGuid():N}");
        var desktop = Path.Combine(profile, "Desktop");
        var rootPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";
        const string policyKey = @"Policies\Microsoft\Windows\System";
        Directory.CreateDirectory(desktop);
        try
        {
            using var software = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(rootPath, writable: true);
            using (var key = software.CreateSubKey(policyKey, writable: true))
            {
                key.SetValue("Planted", @"C:\Pasta da turma", Microsoft.Win32.RegistryValueKind.String);
            }
            var statePath = WriteState(profile, new Midview.Protocol.SavedSetting(
                policyKey, "Planted", Microsoft.Win32.RegistryValueKind.String, @"C:\Outro", @"C:\Pasta da turma"));
            var before = File.ReadAllBytes(statePath);
            File.WriteAllText(_hostsPath, "127.0.0.1 localhost\n");

            UninstallCleanup.Run(_statePath, _hostsPath, new FakeFirewallController(),
                new FakeInputDeviceController(), _netshCalls.Add, studentDesktops: new[] { (desktop, me) },
                openUserSoftware: _ => Microsoft.Win32.Registry.CurrentUser.CreateSubKey(rootPath, writable: true));

            using var read = software.OpenSubKey(policyKey)!;
            Assert.Equal(@"C:\Pasta da turma", read.GetValue("Planted"));
            Assert.Equal(before, File.ReadAllBytes(statePath));
        }
        finally
        {
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(rootPath, throwOnMissingSubKey: false); } catch { }
            ProtectedStudentFolders.Release(desktop, me);
            Directory.Delete(profile, recursive: true);
        }
    }

    // A save location that can't be put back says whose Desktop it is, as the
    // other lines do (the Agent undoes for every student on the machine).
    [Fact]
    public void ASaveLocationThatCannotBePutBack_SaysWhoseDesktop()
    {
        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var profile = Path.Combine(Path.GetTempPath(), $"midview-profile-{Guid.NewGuid():N}");
        var desktop = Path.Combine(profile, "Desktop");
        var rootPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";
        var downloadsPath = $@"{rootPath}\{Midview.Protocol.ClassSaveTargets.DownloadsKey}";
        // Reading stays allowed, so the value is found as Midview left it; putting it back is refused.
        var deny = new System.Security.AccessControl.RegistryAccessRule(me,
            System.Security.AccessControl.RegistryRights.SetValue, System.Security.AccessControl.AccessControlType.Deny);
        Directory.CreateDirectory(desktop);
        try
        {
            var classFolder = Path.Combine(desktop, "Pasta da turma – 3º A");
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(downloadsPath, writable: true))
            {
                key.SetValue(Midview.Protocol.ClassSaveTargets.DownloadsName, classFolder, Microsoft.Win32.RegistryValueKind.ExpandString);
            }
            ChangeDownloadsRules(downloadsPath, security => security.AddAccessRule(deny));
            WriteState(profile, new Midview.Protocol.SavedSetting(
                Midview.Protocol.ClassSaveTargets.DownloadsKey, Midview.Protocol.ClassSaveTargets.DownloadsName,
                Microsoft.Win32.RegistryValueKind.ExpandString, @"%USERPROFILE%\Downloads", classFolder));
            File.WriteAllText(_hostsPath, "127.0.0.1 localhost\n");

            var failures = UninstallCleanup.Run(_statePath, _hostsPath, new FakeFirewallController(),
                new FakeInputDeviceController(), _netshCalls.Add, studentDesktops: new[] { (desktop, me) },
                openUserSoftware: _ => Microsoft.Win32.Registry.CurrentUser.CreateSubKey(rootPath, writable: true));

            var line = Assert.Single(failures);
            Assert.StartsWith($"Pastas de salvar do aluno de {desktop}: ", line);
            Assert.Contains("Downloads", line);
        }
        finally
        {
            // The Deny goes first, or the throwaway key can't be deleted.
            try { ChangeDownloadsRules(downloadsPath, security => security.RemoveAccessRule(deny)); } catch { }
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(rootPath, throwOnMissingSubKey: false); } catch { }
            ProtectedStudentFolders.Release(desktop, me);
            Directory.Delete(profile, recursive: true);
        }
    }

    // The owner may always change the rules, even with SetValue denied.
    private static void ChangeDownloadsRules(string path, Action<System.Security.AccessControl.RegistrySecurity> change)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path,
            Microsoft.Win32.RegistryKeyPermissionCheck.ReadWriteSubTree,
            System.Security.AccessControl.RegistryRights.ChangePermissions | System.Security.AccessControl.RegistryRights.ReadPermissions)!;
        var security = key.GetAccessControl();
        change(security);
        key.SetAccessControl(security);
    }

    // <profile>\AppData\Local\Midview\class-save-locations.json, as the Session leaves it.
    private static string WriteState(string profile, Midview.Protocol.SavedSetting setting)
    {
        var path = Path.Combine(profile, "AppData", "Local", "Midview", Midview.Protocol.ClassSaveLocationsFile.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Midview.Protocol.ClassSaveLocationsFile.Save(path,
            new Midview.Protocol.ClassSaveLocationsState(Guid.NewGuid(), "C:\turma", new[] { setting }));
        return path;
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
