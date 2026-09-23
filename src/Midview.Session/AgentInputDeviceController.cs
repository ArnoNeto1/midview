using Midview.Protocol;

namespace Midview.Session;

// Session runs as the logged-in student, who isn't allowed to disable
// devices, so the actual disabling happens in the Agent (SYSTEM). The Agent
// also re-enables everything by itself if this process dies mid-lock.
public sealed class AgentInputDeviceController : IInputDeviceController, IDisposable
{
    private readonly InputLockPipeClient _client = new();

    public void DisableInputDevices()
    {
        if (!_client.RequestLock())
        {
            Console.WriteLine("Input lock failed: the Agent is unreachable or couldn't disable every keyboard/mouse.");
        }
    }

    public void EnableInputDevices()
    {
        if (!_client.RequestUnlock())
        {
            Console.WriteLine("Input unlock failed: the Agent is unreachable or couldn't re-enable every keyboard/mouse.");
        }
    }

    public void Dispose() => _client.Dispose();
}
