using Midview.Session;
using Xunit;

public class RecordingControlNotice : IControlNotice
{
    public bool Showing { get; private set; }
    public int Shows { get; private set; }
    public bool Throws { get; init; }

    public void Show()
    {
        Shows++;
        Showing = true;
        if (Throws)
        {
            throw new InvalidOperationException("no desktop to show it on");
        }
    }

    public void Hide()
    {
        Showing = false;
        if (Throws)
        {
            throw new InvalidOperationException("no desktop to show it on");
        }
    }
}

public class ControlNoticeTests
{
    private readonly FakeInputDeviceController _devices = new();
    private readonly RecordingControlNotice _notice = new();
    private readonly FakeClock _clock = new();

    private InputLockManager Manager(IControlNotice notice) =>
        new(_devices, _clock, TimeSpan.FromSeconds(5), notice);

    [Fact]
    public void WhileTheTeacherControls_TheStudentIsTold()
    {
        var manager = Manager(_notice);

        manager.StartLock();
        Assert.Equal(1, _devices.DisableCalls);
        Assert.True(_notice.Showing);

        manager.StopLock();
        Assert.Equal(1, _devices.EnableCalls);
        Assert.False(_notice.Showing);
    }

    // Two holds (a reconnect overlapping the connection it replaces) are one
    // notice, and it goes with the last of them.
    [Fact]
    public void TheNotice_StaysUp_UntilTheLastHoldEnds()
    {
        var manager = Manager(_notice);

        manager.StartLock();
        manager.StartLock();
        Assert.Equal(1, _notice.Shows);

        manager.StopLock();
        Assert.True(_notice.Showing);

        manager.StopLock();
        Assert.False(_notice.Showing);
    }

    [Fact]
    public void WhenTheTeacherStopsAnswering_TheNoticeGoesWithTheLock()
    {
        var manager = Manager(_notice);
        manager.StartLock();

        _clock.UtcNow += TimeSpan.FromSeconds(6);
        manager.CheckHeartbeatTimeout();

        Assert.False(manager.IsLocked);
        Assert.False(_notice.Showing);
    }

    // The lock screen says enough; the strip is only for being controlled.
    [Fact]
    public void AScreenLock_ShowsNoControlNotice()
    {
        var manager = Manager(_notice);

        manager.HoldForScreenLock();

        Assert.Equal(1, _devices.DisableCalls);
        Assert.Equal(0, _notice.Shows);
    }

    [Fact]
    public void ANoticeThatFails_NeverStopsTheLockOrItsRelease()
    {
        var manager = Manager(new RecordingControlNotice { Throws = true });

        manager.StartLock();
        Assert.True(manager.IsLocked);

        manager.StopLock();
        Assert.False(manager.IsLocked);
        Assert.Equal(1, _devices.EnableCalls);
    }

    [Fact]
    public void ALockThatCantBeApplied_ShowsNoNotice()
    {
        var manager = new InputLockManager(new FailingInputDeviceController(), _clock, TimeSpan.FromSeconds(5), _notice);

        Assert.ThrowsAny<Exception>(() => manager.StartLock());

        Assert.False(manager.IsLocked);
        Assert.Equal(0, _notice.Shows);
    }

    private sealed class FailingInputDeviceController : IInputDeviceController
    {
        public void DisableInputDevices() => throw new IOException("the Agent didn't answer");

        public void EnableInputDevices()
        {
        }
    }
}
