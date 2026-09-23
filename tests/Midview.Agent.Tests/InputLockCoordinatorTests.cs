using Midview.Agent;
using Xunit;

public class FakeInputDeviceController : IInputDeviceController
{
    public int DisableCalls { get; private set; }
    public int EnableCalls { get; private set; }
    public bool DisableShouldSucceed { get; set; } = true;

    public bool DisableInputDevices()
    {
        DisableCalls++;
        return DisableShouldSucceed;
    }

    public bool EnableInputDevices()
    {
        EnableCalls++;
        return true;
    }
}

public class InputLockCoordinatorTests
{
    [Fact]
    public void Acquire_FirstHolder_DisablesDevices()
    {
        var devices = new FakeInputDeviceController();
        var coordinator = new InputLockCoordinator(devices);

        Assert.True(coordinator.Acquire(new object()));

        Assert.Equal(1, devices.DisableCalls);
    }

    [Fact]
    public void Acquire_SecondHolderOrSameHolderAgain_DoesNotDisableAgain()
    {
        var devices = new FakeInputDeviceController();
        var coordinator = new InputLockCoordinator(devices);
        var first = new object();

        coordinator.Acquire(first);
        coordinator.Acquire(first);
        coordinator.Acquire(new object());

        Assert.Equal(1, devices.DisableCalls);
    }

    [Fact]
    public void Release_OnlyTheLastHolderReEnablesDevices()
    {
        var devices = new FakeInputDeviceController();
        var coordinator = new InputLockCoordinator(devices);
        var first = new object();
        var second = new object();
        coordinator.Acquire(first);
        coordinator.Acquire(second);

        coordinator.Release(first);
        Assert.Equal(0, devices.EnableCalls);

        coordinator.Release(second);
        Assert.Equal(1, devices.EnableCalls);
    }

    [Fact]
    public void Release_HolderThatNeverAcquired_DoesNothing()
    {
        var devices = new FakeInputDeviceController();
        var coordinator = new InputLockCoordinator(devices);
        coordinator.Acquire(new object());

        Assert.True(coordinator.Release(new object()));

        Assert.Equal(0, devices.EnableCalls);
    }

    [Fact]
    public void Acquire_DisableFails_ReturnsFalseButReleaseStillReEnables()
    {
        var devices = new FakeInputDeviceController { DisableShouldSucceed = false };
        var coordinator = new InputLockCoordinator(devices);
        var holder = new object();

        Assert.False(coordinator.Acquire(holder));
        coordinator.Release(holder);

        // Partly-disabled devices must still get re-enabled.
        Assert.Equal(1, devices.EnableCalls);
    }

    [Fact]
    public void ReleaseAll_ReEnablesEvenWithNoHolders_AndForgetsExistingOnes()
    {
        var devices = new FakeInputDeviceController();
        var coordinator = new InputLockCoordinator(devices);
        var holder = new object();
        coordinator.Acquire(holder);

        coordinator.ReleaseAll();
        coordinator.Release(holder);

        Assert.Equal(1, devices.EnableCalls);
    }
}
