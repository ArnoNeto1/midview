namespace Midview.Session;

// Called from two threads at once -- the connection's receive loop
// (Start/Stop/heartbeats) and the heartbeat-check timer -- so every
// operation runs under one lock. Without it, a timeout-driven StopLock could
// interleave with a StartLock and leave the devices disabled while IsLocked
// said otherwise: a stuck lock nothing would ever release.
public sealed class InputLockManager
{
    private readonly IInputDeviceController _deviceController;
    private readonly ISystemClock _clock;
    private readonly TimeSpan _heartbeatTimeout;
    private readonly object _gate = new();
    private DateTimeOffset? _lastHeartbeat;
    private bool _isLocked;

    public InputLockManager(IInputDeviceController deviceController, ISystemClock clock, TimeSpan heartbeatTimeout)
    {
        _deviceController = deviceController;
        _clock = clock;
        _heartbeatTimeout = heartbeatTimeout;
    }

    public bool IsLocked
    {
        get
        {
            lock (_gate)
            {
                return _isLocked;
            }
        }
    }

    public void StartLock()
    {
        lock (_gate)
        {
            if (_isLocked)
            {
                return;
            }
            _deviceController.DisableInputDevices();
            _isLocked = true;
            _lastHeartbeat = _clock.UtcNow;
        }
    }

    public void StopLock()
    {
        lock (_gate)
        {
            StopLockLocked();
        }
    }

    public void OnHeartbeatReceived()
    {
        lock (_gate)
        {
            if (_isLocked)
            {
                _lastHeartbeat = _clock.UtcNow;
            }
        }
    }

    public void CheckHeartbeatTimeout()
    {
        lock (_gate)
        {
            if (!_isLocked || _lastHeartbeat is not { } lastHeartbeat)
            {
                return;
            }
            if (_clock.UtcNow - lastHeartbeat > _heartbeatTimeout)
            {
                StopLockLocked();
            }
        }
    }

    private void StopLockLocked()
    {
        if (!_isLocked)
        {
            return;
        }
        _deviceController.EnableInputDevices();
        _isLocked = false;
        _lastHeartbeat = null;
    }
}
