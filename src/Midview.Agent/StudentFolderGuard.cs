using System.Security.Principal;

namespace Midview.Agent;

// Puts the student's "Arquivos do professor" and "Entregar ao professor"
// back, protected, within one of the Agent's polls of someone deleting them
// (an administrator can; a student can't). Before, they came back only at
// the student's next login, when the Agent starts Session. It also catches
// one the Session made again as the student (a file sent into a folder that
// was gone), which is theirs and so unprotected. Each outcome is logged
// once, so a problem that lasts doesn't fill the log on every poll.
public sealed class StudentFolderGuard
{
    private readonly Func<(string? Desktop, SecurityIdentifier Student)?> _loggedOnStudent;
    private readonly SecurityIdentifier _owner;
    private readonly Action<string> _log;
    private string? _lastOutcome;

    // loggedOnStudent: the student at the console and their Desktop (null
    // when there is none the Agent may work on), or null when nobody is
    // logged on (UserSessionProcessLauncher.LoggedOnStudent). owner: who Protect makes
    // the folders' owner, SYSTEM outside the tests.
    public StudentFolderGuard(
        Func<(string? Desktop, SecurityIdentifier Student)?> loggedOnStudent, SecurityIdentifier owner,
        Action<string>? log = null)
    {
        _loggedOnStudent = loggedOnStudent;
        _owner = owner;
        _log = log ?? AgentLog.Warn;
    }

    // Never throws: it runs in the loop that keeps Session running.
    public void Check()
    {
        try
        {
            // No Desktop to work on (none, or refused: StudentDesktopCheck logs that once): nothing to put back.
            if (_loggedOnStudent() is not { Desktop: { } desktop } student || !ProtectedStudentFolders.NeedsProtecting(desktop, _owner))
            {
                _lastOutcome = null;
                return;
            }

            var problems = ProtectedStudentFolders.Protect(desktop, student.Student, _owner);
            Report(problems.Count == 0
                ? $"The student's folders on {desktop} were missing or not protected: put back and protected."
                : $"The student's folders on {desktop} couldn't be put back: {string.Join("; ", problems)}");
        }
        catch (Exception ex)
        {
            Report($"Checking the student's folders failed: {ex.Message}");
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
