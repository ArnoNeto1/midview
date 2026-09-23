namespace Midview.Agent;

public interface IInputDeviceController
{
    // Both return false if any device failed to change state.
    bool DisableInputDevices();
    bool EnableInputDevices();
}
