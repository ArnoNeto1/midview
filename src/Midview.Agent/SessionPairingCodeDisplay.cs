using System.ComponentModel;
using System.Diagnostics;
using Midview.Protocol;

namespace Midview.Agent;

// Shows the pairing code by starting the Session in the student's session
// with PairingCodeArguments, and takes it down by ending that process. The
// Agent runs as SYSTEM in session 0 and can't show a window itself.
public sealed class SessionPairingCodeDisplay : IPairingCodeDisplay
{
    private readonly UserSessionProcessLauncher _launcher;

    public SessionPairingCodeDisplay(UserSessionProcessLauncher launcher)
    {
        _launcher = launcher;
    }

    public bool SomeoneLoggedOn => UserSessionProcessLauncher.SomeoneLoggedOn();

    public IDisposable? Show(int code, string teacherName) =>
        _launcher.TryLaunchWithArguments(PairingCodeArguments.Build(code, teacherName), out var processId)
            ? new CodeWindow(processId)
            : null;

    private sealed class CodeWindow(int processId) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                process.Kill();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // Already gone (it closes itself after the timeout).
            }
        }
    }
}
