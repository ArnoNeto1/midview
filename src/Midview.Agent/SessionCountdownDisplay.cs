using System.ComponentModel;
using System.Diagnostics;
using Midview.Protocol;

namespace Midview.Agent;

// Shows the countdown banner by starting the Session in the student's
// session with CountdownArguments, and takes it down by ending that
// process, as SessionPairingCodeDisplay does for the pairing code. Nobody
// logged on means nobody to warn: nothing is shown.
public sealed class SessionCountdownDisplay : ICountdownDisplay
{
    private readonly UserSessionProcessLauncher _launcher;
    private readonly object _gate = new();
    private int? _processId;

    public SessionCountdownDisplay(UserSessionProcessLauncher launcher)
    {
        _launcher = launcher;
    }

    public void Show(PowerCountdown kind, int seconds)
    {
        lock (_gate)
        {
            EndShown();
            try
            {
                _processId = _launcher.TryLaunchWithArguments(CountdownArguments.Build(kind, seconds), out var processId)
                    ? processId
                    : null;
            }
            catch (Exception ex)
            {
                AgentLog.Warn($"The countdown banner couldn't be shown: {ex.Message}");
                _processId = null;
            }
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            EndShown();
        }
    }

    private void EndShown()
    {
        if (_processId is not { } processId)
        {
            return;
        }
        _processId = null;
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // Already gone (the student signed out, or it ended by itself).
        }
    }
}
