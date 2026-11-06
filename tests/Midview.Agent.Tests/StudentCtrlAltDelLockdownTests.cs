using Microsoft.Win32;
using Midview.Agent;
using Xunit;

// Each student's hive is a throwaway HKCU subtree (HKCU\Software\MidviewTest\
// <guid>\<sid> standing in for HKEY_USERS\<sid>\Software), and the record a
// temp file, so the real policy keys and the real ProgramData are never touched.
public sealed class StudentCtrlAltDelLockdownTests : IDisposable
{
    private const string SystemPath = @"Microsoft\Windows\CurrentVersion\Policies\System";
    private const string ExplorerPath = @"Microsoft\Windows\CurrentVersion\Policies\Explorer";
    private const string Ana = "S-1-5-21-1-2-3-1001";
    private const string Bia = "S-1-5-21-1-2-3-1002";

    private readonly string _rootPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";
    private readonly string _statePath = Path.Combine(Path.GetTempPath(), $"midview-cad-{Guid.NewGuid():N}.json");
    private readonly List<string> _log = new();
    private string? _loggedOn = Ana;

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false);
        File.Delete(_statePath);
    }

    private StudentCtrlAltDelLockdown Lockdown() =>
        new(() => _loggedOn, sid => Registry.CurrentUser.CreateSubKey($@"{_rootPath}\{sid}", writable: true), _statePath, _log.Add);

    private object? Read(string sid, string path, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{_rootPath}\{sid}\{path}");
        return key?.GetValue(name);
    }

    private void Write(string sid, string path, string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"{_rootPath}\{sid}\{path}", writable: true);
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    [Fact]
    public void Block_TurnsOffTaskManagerAndSignOut_ForTheStudentLoggedOn()
    {
        Lockdown().Block();

        Assert.Equal(1, Read(Ana, SystemPath, "DisableTaskMgr"));
        Assert.Equal(1, Read(Ana, ExplorerPath, "NoLogoff"));
    }

    [Fact]
    public void Unblock_GivesBothBack()
    {
        var lockdown = Lockdown();
        lockdown.Block();

        lockdown.Unblock();

        Assert.Null(Read(Ana, SystemPath, "DisableTaskMgr"));
        Assert.Null(Read(Ana, ExplorerPath, "NoLogoff"));
    }

    // A school that already keeps the Task Manager off (its own policy) keeps it
    // off: only what Midview wrote is taken back.
    [Fact]
    public void AValueTheSchoolAlreadySet_IsLeftAfterTheLock()
    {
        Write(Ana, SystemPath, "DisableTaskMgr", 1);
        var lockdown = Lockdown();

        lockdown.Block();
        lockdown.Unblock();

        Assert.Equal(1, Read(Ana, SystemPath, "DisableTaskMgr"));
        Assert.Null(Read(Ana, ExplorerPath, "NoLogoff"));
    }

    // The Agent crashed (or the machine lost power) mid-lock: the next Agent
    // isn't locking, and takes back what the last one wrote on its first pass.
    [Fact]
    public void ALockLeftByACrash_IsTakenBackOnTheNextPass()
    {
        Lockdown().Block();

        Lockdown().Keep();

        Assert.Null(Read(Ana, SystemPath, "DisableTaskMgr"));
        Assert.Null(Read(Ana, ExplorerPath, "NoLogoff"));
    }

    // The leftover is in a student's own hive, which is only there while they
    // are logged on: it waits for them, and another student is left alone.
    [Fact]
    public void ALeftoverOfAStudentNotLoggedOn_WaitsForThem()
    {
        Lockdown().Block();
        _loggedOn = Bia;
        var next = Lockdown();

        next.Keep();
        Assert.Equal(1, Read(Ana, SystemPath, "DisableTaskMgr"));
        Assert.Null(Read(Bia, SystemPath, "DisableTaskMgr"));

        _loggedOn = Ana;
        next.Keep();
        Assert.Null(Read(Ana, SystemPath, "DisableTaskMgr"));
        Assert.Null(Read(Ana, ExplorerPath, "NoLogoff"));
    }

    // Locked with nobody logged on: whoever logs on during the lock is locked
    // down on the next pass.
    [Fact]
    public void AStudentWhoLogsOnDuringTheLock_IsLockedDownOnTheNextPass()
    {
        _loggedOn = null;
        var lockdown = Lockdown();
        lockdown.Block();

        _loggedOn = Bia;
        lockdown.Keep();

        Assert.Equal(1, Read(Bia, SystemPath, "DisableTaskMgr"));
        Assert.Equal(1, Read(Bia, ExplorerPath, "NoLogoff"));
    }

    // Nothing here may throw: it runs in the Agent's loop and in the lock's hold.
    [Fact]
    public void ARegistryThatRefusesEverything_NeverThrows()
    {
        var lockdown = new StudentCtrlAltDelLockdown(
            () => Ana, _ => throw new UnauthorizedAccessException("negado"), _statePath, _log.Add);

        lockdown.Block();
        lockdown.Keep();
        lockdown.Unblock();

        Assert.Contains(_log, line => line.Contains("negado"));
    }
}
