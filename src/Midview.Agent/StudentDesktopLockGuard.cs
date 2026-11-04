using System.Security.Principal;
using Midview.Protocol;

namespace Midview.Agent;

// Keeps "Travar a Área de Trabalho" (LabConfig.LockDesktop) on whoever is
// logged on: on every poll of the loop that keeps Session running
// (LabConfigCommandHandler.KeepDesktopLock), and right after the teacher
// turns it on or off. Locking is StudentDesktopLock's and writes nothing
// where everything is already locked; turned off, only the Desktop's own
// permissions are read unless there is something to give back. Each outcome
// is logged once, as StudentFolderGuard does, so a problem that lasts doesn't
// fill the log on every poll.
public sealed class StudentDesktopLockGuard
{
    private readonly Func<(string? Desktop, SecurityIdentifier Student)?> _loggedOnStudent;
    private readonly Action<string> _log;
    private readonly bool _guardPermissions;
    private readonly object _gate = new();
    private string? _lastOutcome;

    // loggedOnStudent: the student at the console and their Desktop (null
    // when there is none the Agent may work on), or null when nobody is
    // logged on (UserSessionProcessLauncher.LoggedOnStudent). guardPermissions:
    // StudentDesktopLock.Lock's, always true outside the tests.
    public StudentDesktopLockGuard(
        Func<(string? Desktop, SecurityIdentifier Student)?> loggedOnStudent, Action<string>? log = null,
        bool guardPermissions = true)
    {
        _loggedOnStudent = loggedOnStudent;
        _log = log ?? AgentLog.Warn;
        _guardPermissions = guardPermissions;
    }

    // The Desktop locked (locked) or given back. Returns what couldn't be
    // done, for the teacher's panel. Never throws.
    public IReadOnlyList<string> Check(bool locked)
    {
        lock (_gate)
        {
            try
            {
                // Nobody, or a Desktop the Agent may not work on (StudentDesktopCheck logs that once).
                if (_loggedOnStudent() is not { Desktop: { } desktop } student
                    || (!locked && !StudentDesktopLock.IsAnyLocked(desktop, student.Student)))
                {
                    _lastOutcome = null;
                    return Array.Empty<string>();
                }

                var result = locked
                    ? StudentDesktopLock.Lock(desktop, student.Student, _guardPermissions)
                    : StudentDesktopLock.Unlock(desktop, student.Student);
                if (result.Changed == 0 && result.Problems.Count == 0)
                {
                    _lastOutcome = null;
                    return result.Problems;
                }

                var what = locked ? "locked" : "unlocked";
                Report(result.Problems.Count == 0
                    ? $"The student's Desktop ({desktop}) was {what}: {result.Changed} change(s)."
                    : $"The student's Desktop ({desktop}) couldn't be fully {what}: {string.Join("; ", result.Problems)}");
                return result.Problems;
            }
            catch (Exception ex)
            {
                Report($"Keeping the student's Desktop lock failed: {ex.Message}");
                return new[] { ErrorText.Describe(ex) };
            }
        }
    }

    private void Report(string outcome)
    {
        if (outcome != _lastOutcome)
        {
            _lastOutcome = outcome;
            _log(outcome);
        }
    }
}
