namespace Midview.Session;

// The student's keyboard and mouse are locked while the teacher controls this
// computer (StartLock/StopLock, kept up by heartbeats) or while its screen is
// locked (one hold per screen-lock connection); they come back only when
// neither holds them, so stopping control never undoes a screen lock or the
// other way round.
//
// Called from several threads at once -- the connections' receive loops and
// the heartbeat-check timer -- so every operation runs under one lock.
// Without it, a timeout-driven StopLock could interleave with a StartLock and
// leave the devices disabled while IsLocked said otherwise: a stuck lock
// nothing would ever release.
public sealed class InputLockManager
{
    private readonly IInputDeviceController _deviceController;
    private readonly ISystemClock _clock;
    private readonly TimeSpan _heartbeatTimeout;
    private readonly IControlNotice? _controlNotice;
    private readonly object _gate = new();
    private DateTimeOffset? _lastHeartbeat;
    private bool _isLocked;
    private int _screenLocks;
    private bool _devicesDisabled;

    // The notice tells the student they're being controlled; it follows
    // control only, never a screen lock, whose own screen says enough.
    public InputLockManager(
        IInputDeviceController deviceController, ISystemClock clock, TimeSpan heartbeatTimeout,
        IControlNotice? controlNotice = null)
    {
        _deviceController = deviceController;
        _clock = clock;
        _heartbeatTimeout = heartbeatTimeout;
        _controlNotice = controlNotice;
    }

    // Whether the teacher is controlling (not whether the devices are off).
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

            DisableDevices();
            _isLocked = true;
            _lastHeartbeat = _clock.UtcNow;
            TryNotice(notice => notice.Show());
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

    public void HoldForScreenLock()
    {
        lock (_gate)
        {
            DisableDevices();
            _screenLocks++;
        }
    }

    public void ReleaseScreenLock()
    {
        lock (_gate)
        {
            if (_screenLocks == 0)
            {
                return;
            }

            if (_screenLocks == 1 && !_isLocked)
            {
                EnableDevices();
            }
            _screenLocks--;
        }
    }

    private void StopLockLocked()
    {
        if (!_isLocked)
        {
            return;
        }

        if (_screenLocks == 0)
        {
            EnableDevices();
        }
        _isLocked = false;
        _lastHeartbeat = null;
        TryNotice(notice => notice.Hide());
    }

    // Each changes the flag only once the call has gone through, so a
    // device change that fails leaves the state as it really is.
    private void DisableDevices()
    {
        if (!_devicesDisabled)
        {
            _deviceController.DisableInputDevices();
            _devicesDisabled = true;
        }
    }

    private void EnableDevices()
    {
        if (_devicesDisabled)
        {
            _deviceController.EnableInputDevices();
            _devicesDisabled = false;
        }
    }

    // A notice that fails never gets in the lock's way.
    private void TryNotice(Action<IControlNotice> action)
    {
        if (_controlNotice is null)
        {
            return;
        }

        try
        {
            action(_controlNotice);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"The control notice failed: {ex.Message}");
        }
    }
}
