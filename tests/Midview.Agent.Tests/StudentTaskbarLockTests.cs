using Microsoft.Win32;
using Midview.Agent;
using Xunit;

// Each student's hive is a throwaway HKCU subtree (HKCU\Software\MidviewTest\
// <guid>\<sid> standing in for HKEY_USERS\<sid>\Software), and the record a
// temp file, so the real policy keys and the real ProgramData are never touched.
public sealed class StudentTaskbarLockTests : IDisposable
{
    private const string ExplorerPath = StudentTaskbarLock.ExplorerPath;
    private const string Value = StudentTaskbarLock.ValueName;
    private const string Ana = "S-1-5-21-1-2-3-1001";
    private const string Bia = "S-1-5-21-1-2-3-1002";

    private readonly string _rootPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";
    private readonly string _statePath = Path.Combine(Path.GetTempPath(), $"midview-tb-lock-{Guid.NewGuid():N}.json");
    private readonly List<string> _log = new();
    private string? _loggedOn = Ana;

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false);
        File.Delete(_statePath);
    }

    private StudentTaskbarLock Lock() =>
        new(() => _loggedOn, sid => Registry.CurrentUser.CreateSubKey($@"{_rootPath}\{sid}", writable: true), _statePath, _log.Add);

    private object? Read(string sid)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{_rootPath}\{sid}\{ExplorerPath}");
        return key?.GetValue(Value);
    }

    private void Write(string sid, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"{_rootPath}\{sid}\{ExplorerPath}", writable: true);
        key.SetValue(Value, value, RegistryValueKind.DWord);
    }

    [Fact]
    public void On_BlocksPinningForTheStudentLoggedOn()
    {
        Lock().Keep(locked: true);

        Assert.Equal(1, Read(Ana));
    }

    [Fact]
    public void Off_GivesItBack()
    {
        var taskbar = Lock();
        taskbar.Keep(locked: true);

        taskbar.Keep(locked: false);

        Assert.Null(Read(Ana));
        Assert.False(File.Exists(_statePath));
    }

    // The value is kept in the student's hive, so a pin the student undid by
    // hand (or a script that deleted it) comes back on the next pass.
    [Fact]
    public void On_PutsItBackOnTheNextPass()
    {
        var taskbar = Lock();
        taskbar.Keep(locked: true);
        Write(Ana, 0);

        taskbar.Keep(locked: true);

        Assert.Equal(1, Read(Ana));
    }

    // A school that already blocks pinning (its own policy) keeps it blocked:
    // only what Midview wrote is taken back.
    [Fact]
    public void AValueTheSchoolAlreadySet_IsLeftAfterTheLock()
    {
        Write(Ana, 1);
        var taskbar = Lock();

        taskbar.Keep(locked: true);
        taskbar.Keep(locked: false);

        Assert.Equal(1, Read(Ana));
    }

    // Turned off while the student isn't logged on: it waits for them, and
    // another student is left alone.
    [Fact]
    public void TurnedOffWhileAStudentIsAway_IsGivenBackWhenTheyLogOn()
    {
        Lock().Keep(locked: true);
        _loggedOn = Bia;
        var next = Lock();

        next.Keep(locked: false);
        Assert.Equal(1, Read(Ana));
        Assert.Null(Read(Bia));

        _loggedOn = Ana;
        next.Keep(locked: false);
        Assert.Null(Read(Ana));
    }

    // Every student who logs on while it is on is blocked, and each one is
    // given it back when it is turned off.
    [Fact]
    public void EachStudentWhoLogsOnDuringTheLock_IsBlockedAndGivenBack()
    {
        var taskbar = Lock();
        taskbar.Keep(locked: true);
        _loggedOn = Bia;
        taskbar.Keep(locked: true);
        Assert.Equal(1, Read(Ana));
        Assert.Equal(1, Read(Bia));

        taskbar.Keep(locked: false);
        _loggedOn = Ana;
        taskbar.Keep(locked: false);

        Assert.Null(Read(Ana));
        Assert.Null(Read(Bia));
    }

    [Fact]
    public void NobodyLoggedOn_WritesNothing()
    {
        _loggedOn = null;

        Lock().Keep(locked: true);

        Assert.False(File.Exists(_statePath));
    }

    // Nothing here may throw: it runs in the Agent's loop. A failure that
    // lasts is logged once, not on every poll.
    [Fact]
    public void ARegistryThatRefusesEverything_NeverThrows_AndLogsOnce()
    {
        var taskbar = new StudentTaskbarLock(
            () => Ana, _ => throw new UnauthorizedAccessException("negado"), _statePath, _log.Add);

        taskbar.Keep(locked: true);
        taskbar.Keep(locked: true);
        taskbar.Keep(locked: false);

        Assert.Single(_log, line => line.Contains("negado"));
    }
}
