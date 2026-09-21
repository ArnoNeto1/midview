namespace Midview.Session;

public sealed class InputLockManager
{
    private readonly IInputDeviceController _deviceController;
    private readonly ISystemClock _clock;
    private readonly TimeSpan _heartbeatTimeout;
    private DateTimeOffset? _lastHeartbeat;

    public InputLockManager(IInputDeviceController deviceController, ISystemClock clock, TimeSpan heartbeatTimeout)
    {
        _deviceController = deviceController;
        _clock = clock;
        _heartbeatTimeout = heartbeatTimeout;
    }

    public bool IsLocked { get; private set; }

    public void StartLock()
    {
        if (IsLocked)
        {
            return;
        }
        _deviceController.DisableInputDevices();
        IsLocked = true;
        _lastHeartbeat = _clock.UtcNow;
    }

    public void StopLock()
    {
        if (!IsLocked)
        {
            return;
        }
        _deviceController.EnableInputDevices();
        IsLocked = false;
        _lastHeartbeat = null;
    }

    public void OnHeartbeatReceived()
    {
        if (IsLocked)
        {
            _lastHeartbeat = _clock.UtcNow;
        }
    }

    public void CheckHeartbeatTimeout()
    {
        if (!IsLocked || _lastHeartbeat is null)
        {
            return;
        }
        if (_clock.UtcNow - _lastHeartbeat.Value > _heartbeatTimeout)
        {
            StopLock();
        }
    }
}
