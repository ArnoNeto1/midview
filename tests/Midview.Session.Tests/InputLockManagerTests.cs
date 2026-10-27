using Midview.Session;
using Xunit;

public class FakeClock : ISystemClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
}

public class FakeInputDeviceController : IInputDeviceController
{
    public int DisableCalls { get; private set; }
    public int EnableCalls { get; private set; }
    public void DisableInputDevices() => DisableCalls++;
    public void EnableInputDevices() => EnableCalls++;
}

public class InputLockManagerTests
{
    [Fact]
    public void StartLock_DisablesInputDevicesOnceEvenIfCalledTwice()
    {
        var controller = new FakeInputDeviceController();
        var manager = new InputLockManager(controller, new FakeClock(), TimeSpan.FromSeconds(5));

        manager.StartLock();
        manager.StartLock();

        Assert.Equal(1, controller.DisableCalls);
        Assert.True(manager.IsLocked);
    }

    [Fact]
    public void StopLock_EnablesInputDevices()
    {
        var controller = new FakeInputDeviceController();
        var manager = new InputLockManager(controller, new FakeClock(), TimeSpan.FromSeconds(5));
        manager.StartLock();

        manager.StopLock();

        Assert.Equal(1, controller.EnableCalls);
        Assert.False(manager.IsLocked);
    }

    [Fact]
    public void CheckHeartbeatTimeout_NoHeartbeatPastTimeout_AutoUnlocks()
    {
        var controller = new FakeInputDeviceController();
        var clock = new FakeClock();
        var manager = new InputLockManager(controller, clock, TimeSpan.FromSeconds(5));
        manager.StartLock();

        clock.UtcNow = clock.UtcNow.AddSeconds(6);
        manager.CheckHeartbeatTimeout();

        Assert.False(manager.IsLocked);
        Assert.Equal(1, controller.EnableCalls);
    }

    [Fact]
    public void CheckHeartbeatTimeout_HeartbeatReceivedInTime_StaysLocked()
    {
        var controller = new FakeInputDeviceController();
        var clock = new FakeClock();
        var manager = new InputLockManager(controller, clock, TimeSpan.FromSeconds(5));
        manager.StartLock();

        clock.UtcNow = clock.UtcNow.AddSeconds(3);
        manager.OnHeartbeatReceived();
        clock.UtcNow = clock.UtcNow.AddSeconds(3);
        manager.CheckHeartbeatTimeout();

        Assert.True(manager.IsLocked);
        Assert.Equal(0, controller.EnableCalls);
    }

    [Fact]
    public void CheckHeartbeatTimeout_NotLocked_DoesNothing()
    {
        var controller = new FakeInputDeviceController();
        var clock = new FakeClock();
        var manager = new InputLockManager(controller, clock, TimeSpan.FromSeconds(5));

        manager.CheckHeartbeatTimeout();

        Assert.Equal(0, controller.EnableCalls);
        Assert.Equal(0, controller.DisableCalls);
    }

    [Fact]
    public void AScreenLock_KeepsTheDevicesDisabled_WhenControlStops()
    {
        var devices = new FakeInputDeviceController();
        var manager = new InputLockManager(devices, new FakeClock(), TimeSpan.FromSeconds(5));
        manager.HoldForScreenLock();
        manager.StartLock();

        manager.StopLock();
        Assert.Equal(1, devices.DisableCalls);
        Assert.Equal(0, devices.EnableCalls);

        manager.ReleaseScreenLock();
        Assert.Equal(1, devices.EnableCalls);
    }

    [Fact]
    public void Control_KeepsTheDevicesDisabled_WhenAScreenLockEnds()
    {
        var devices = new FakeInputDeviceController();
        var manager = new InputLockManager(devices, new FakeClock(), TimeSpan.FromSeconds(5));
        manager.StartLock();
        manager.HoldForScreenLock();

        manager.ReleaseScreenLock();
        Assert.Equal(0, devices.EnableCalls);
        Assert.True(manager.IsLocked);

        manager.StopLock();
        Assert.Equal(1, devices.EnableCalls);
    }

    // Two teachers, or a reconnect overlapping the connection it replaces: each
    // hold is its own, and the last one to end gives the devices back.
    [Fact]
    public void EachControlHold_IsReleasedOnItsOwn()
    {
        var devices = new FakeInputDeviceController();
        var manager = new InputLockManager(devices, new FakeClock(), TimeSpan.FromSeconds(5));
        manager.StartLock();
        manager.StartLock();

        manager.StopLock();
        Assert.True(manager.IsLocked);
        Assert.Equal(0, devices.EnableCalls);

        manager.StopLock();
        manager.StopLock(); // one too many: nothing to release
        Assert.False(manager.IsLocked);
        Assert.Equal(1, devices.DisableCalls);
        Assert.Equal(1, devices.EnableCalls);
    }

    // A stop that has no hold to end leaves nothing behind that a hold of its
    // own would later trip over.
    [Fact]
    public void AStopWithNoHold_ChangesNothing()
    {
        var devices = new FakeInputDeviceController();
        var manager = new InputLockManager(devices, new FakeClock(), TimeSpan.FromSeconds(5));

        manager.StopLock();
        Assert.Equal(0, devices.EnableCalls);

        manager.StartLock();
        Assert.True(manager.IsLocked);

        manager.StopLock();
        Assert.False(manager.IsLocked);
        Assert.Equal(1, devices.EnableCalls);
    }

    // A hold whose lock the timeout ended is still a hold, with its connection
    // open: when it ends at last it must not take a newer hold's lock with it.
    [Fact]
    public void ALateStop_FromAHoldTheTimeoutEnded_LeavesANewerHoldLocked()
    {
        var devices = new FakeInputDeviceController();
        var clock = new FakeClock();
        var manager = new InputLockManager(devices, clock, TimeSpan.FromSeconds(5));
        manager.StartLock(); // the old connection
        clock.UtcNow = clock.UtcNow.AddSeconds(6);
        manager.CheckHeartbeatTimeout();
        Assert.False(manager.IsLocked);

        manager.StartLock(); // the new one
        manager.StopLock(); // the old one ends at last

        Assert.True(manager.IsLocked);
        Assert.Equal(1, devices.EnableCalls); // the timeout's, and no other

        manager.StopLock();
        Assert.False(manager.IsLocked);
        Assert.Equal(2, devices.EnableCalls);
    }

    // The older hold's Master may be gone, and its timeout must not end the
    // lock just before the new hold's first heartbeat: a hold that starts is
    // as alive as one that sends a heartbeat.
    [Fact]
    public void AHoldThatJoinsAnother_CountsAsAHeartbeat()
    {
        var devices = new FakeInputDeviceController();
        var clock = new FakeClock();
        var manager = new InputLockManager(devices, clock, TimeSpan.FromSeconds(5));
        manager.StartLock();
        clock.UtcNow = clock.UtcNow.AddSeconds(4);
        manager.StartLock();

        clock.UtcNow = clock.UtcNow.AddSeconds(4);
        manager.CheckHeartbeatTimeout();

        Assert.True(manager.IsLocked);
        Assert.Equal(0, devices.EnableCalls);
    }

    [Fact]
    public void TheDevicesComeBack_OnlyWhenTheLastControlHoldAndEveryScreenLockHaveEnded()
    {
        var devices = new FakeInputDeviceController();
        var manager = new InputLockManager(devices, new FakeClock(), TimeSpan.FromSeconds(5));
        manager.StartLock();
        manager.StartLock();
        manager.HoldForScreenLock();

        manager.ReleaseScreenLock();
        manager.StopLock();
        Assert.True(manager.IsLocked);
        Assert.Equal(0, devices.EnableCalls);

        manager.StopLock();
        Assert.False(manager.IsLocked);
        Assert.Equal(1, devices.DisableCalls);
        Assert.Equal(1, devices.EnableCalls);
    }

    // Two teachers, or a reconnect overlapping the old connection.
    [Fact]
    public void EachScreenLock_IsReleasedOnItsOwn()
    {
        var devices = new FakeInputDeviceController();
        var manager = new InputLockManager(devices, new FakeClock(), TimeSpan.FromSeconds(5));
        manager.HoldForScreenLock();
        manager.HoldForScreenLock();

        manager.ReleaseScreenLock();
        Assert.Equal(0, devices.EnableCalls);

        manager.ReleaseScreenLock();
        manager.ReleaseScreenLock(); // one too many: nothing to release
        Assert.Equal(1, devices.DisableCalls);
        Assert.Equal(1, devices.EnableCalls);
    }

    // A screen lock ends with its own connection, never with control's
    // heartbeats.
    [Fact]
    public void AHeartbeatTimeout_EndsControl_ButNotAScreenLock()
    {
        var devices = new FakeInputDeviceController();
        var clock = new FakeClock();
        var manager = new InputLockManager(devices, clock, TimeSpan.FromSeconds(5));
        manager.HoldForScreenLock();
        manager.StartLock();

        clock.UtcNow = clock.UtcNow.AddSeconds(6);
        manager.CheckHeartbeatTimeout();

        Assert.False(manager.IsLocked);
        Assert.Equal(0, devices.EnableCalls);
    }

    // Each thread is a connection: it ends only holds it started, and ends
    // the ones it still has when it is done, as a connection's clean-up does.
    [Fact]
    public void ConcurrentCallsFromReceiveLoopAndTimer_NeverLeaveDevicesDisabledOnceEveryHoldEnds()
    {
        var devices = new FakeInputDeviceController();
        // A real clock with a zero timeout: every timeout check that finds the
        // lock held releases it, so the timer thread keeps racing the others.
        var manager = new InputLockManager(devices, new SystemClock(), TimeSpan.Zero);

        var threads = Enumerable.Range(0, 4).Select(seed => new Thread(() =>
        {
            var random = new Random(seed);
            var holds = 0;
            for (var i = 0; i < 5000; i++)
            {
                switch (random.Next(4))
                {
                    case 0:
                        manager.StartLock();
                        holds++;
                        break;
                    case 1:
                        if (holds > 0)
                        {
                            manager.StopLock();
                            holds--;
                        }
                        break;
                    case 2: manager.OnHeartbeatReceived(); break;
                    default: manager.CheckHeartbeatTimeout(); break;
                }
            }

            for (; holds > 0; holds--)
            {
                manager.StopLock();
            }
        })).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        Assert.False(manager.IsLocked);
        Assert.Equal(devices.DisableCalls, devices.EnableCalls);
    }
}
