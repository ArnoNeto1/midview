using TreezeSuite.Session;
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
}
