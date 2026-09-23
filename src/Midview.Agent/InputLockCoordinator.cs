namespace Midview.Agent;

// Input stays disabled while at least one holder (one Session connection)
// asks for it, and is re-enabled when the last one releases. A holder is
// recorded even when disabling only partly succeeded, so that releasing
// always re-enables whatever did get disabled.
public sealed class InputLockCoordinator
{
    private readonly IInputDeviceController _devices;
    private readonly HashSet<object> _holders = new();
    private readonly object _gate = new();

    public InputLockCoordinator(IInputDeviceController devices)
    {
        _devices = devices;
    }

    public bool Acquire(object holder)
    {
        lock (_gate)
        {
            var isFirst = _holders.Count == 0;
            if (!_holders.Add(holder) || !isFirst)
            {
                return true;
            }
            return _devices.DisableInputDevices();
        }
    }

    public bool Release(object holder)
    {
        lock (_gate)
        {
            if (!_holders.Remove(holder) || _holders.Count > 0)
            {
                return true;
            }
            return _devices.EnableInputDevices();
        }
    }

    public bool ReleaseAll()
    {
        lock (_gate)
        {
            _holders.Clear();
            return _devices.EnableInputDevices();
        }
    }
}
