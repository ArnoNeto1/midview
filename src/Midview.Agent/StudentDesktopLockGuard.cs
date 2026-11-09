using System.Security.Principal;
using Microsoft.Win32;
using Midview.Protocol;

namespace Midview.Agent;

// Keeps "Travar a Área de Trabalho" (LabConfig.LockDesktop) on whoever is
// logged on: on every poll of the loop that keeps Session running
// (LabConfigCommandHandler.KeepDesktopLock), and right after the teacher
// turns it on or off. The files and folders are StudentDesktopLock's, the
// names of Windows' own icons (Lixeira...) StudentDesktopNamesLock's; the
// icons' places are kept by the student's Session. Locking writes nothing
// where everything is already locked; turned off, only the Desktop's own
// permissions are read unless there is something to give back. Each outcome
// is logged once, as StudentFolderGuard does, so a problem that lasts doesn't
// fill the log on every poll.
public sealed class StudentDesktopLockGuard
{
    private readonly Func<(string? Desktop, SecurityIdentifier Student)?> _loggedOnStudent;
    private readonly Func<SecurityIdentifier, RegistryKey?>? _openSoftware;
    private readonly Action<string> _log;
    private readonly bool _guardPermissions;
    private readonly object _gate = new();
    private string? _lastOutcome;
    private string? _lastNamesOutcome;

    // loggedOnStudent: the student at the console and their Desktop (null
    // when there is none the Agent may work on), or null when nobody is
    // logged on (UserSessionProcessLauncher.LoggedOnStudent). guardPermissions:
    // StudentDesktopLock.Lock's, always true outside the tests. openSoftware:
    // the student's Software key, writable, for the names of Windows' own
    // icons (StudentDesktopNamesLock); null leaves them alone.
    public StudentDesktopLockGuard(
        Func<(string? Desktop, SecurityIdentifier Student)?> loggedOnStudent, Action<string>? log = null,
        bool guardPermissions = true, Func<SecurityIdentifier, RegistryKey?>? openSoftware = null)
    {
        _loggedOnStudent = loggedOnStudent;
        _openSoftware = openSoftware;
        _log = log ?? AgentLog.Warn;
        _guardPermissions = guardPermissions;
    }

    // The student's own Software key in their hive, writable; null when it
    // isn't loaded.
    public static RegistryKey? OpenStudentSoftware(SecurityIdentifier student) =>
        Registry.Users.OpenSubKey($@"{student.Value}\Software", writable: true);

    // The Desktop locked (locked) or given back. Returns what couldn't be
    // done, for the teacher's panel. Never throws.
    public IReadOnlyList<string> Check(bool locked)
    {
        lock (_gate)
        {
            (string? Desktop, SecurityIdentifier Student)? student;
            try
            {
                student = _loggedOnStudent();
            }
            catch (Exception ex)
            {
                Report($"Keeping the student's Desktop lock failed: {ex.Message}");
                return new[] { ErrorText.Describe(ex) };
            }
            var files = CheckFiles(student, locked);
            var names = student is { } who ? CheckNames(who.Student, locked) : Array.Empty<string>();
            return names.Count == 0 ? files : files.Concat(names).ToList();
        }
    }

    // Lixeira, Este Computador...: their names, in the student's registry.
    private IReadOnlyList<string> CheckNames(SecurityIdentifier student, bool locked)
    {
        if (_openSoftware is null)
        {
            return Array.Empty<string>();
        }
        try
        {
            using var software = _openSoftware(student);
            if (software is null || (!locked && !StudentDesktopNamesLock.IsAnyLocked(software, student)))
            {
                _lastNamesOutcome = null;
                return Array.Empty<string>();
            }
            var result = locked
                ? StudentDesktopNamesLock.Lock(software, student, _guardPermissions)
                : StudentDesktopNamesLock.Unlock(software, student);
            if (result.Changed == 0 && result.Problems.Count == 0)
            {
                _lastNamesOutcome = null;
                return result.Problems;
            }
            ReportNames(result.Problems.Count == 0
                ? $"The names of the student's Windows Desktop icons were {(locked ? "locked" : "unlocked")}: {result.Changed} change(s)."
                : $"The names of the student's Windows Desktop icons couldn't be fully {(locked ? "locked" : "unlocked")}: {string.Join("; ", result.Problems)}");
            return result.Problems;
        }
        catch (Exception ex)
        {
            ReportNames($"Keeping the names of the student's Windows Desktop icons failed: {ex.Message}");
            return new[] { ErrorText.Describe(ex) };
        }
    }

    // The Desktop's files and folders (StudentDesktopLock).
    private IReadOnlyList<string> CheckFiles((string? Desktop, SecurityIdentifier Student)? loggedOn, bool locked)
    {
        try
        {
            // Nobody, or a Desktop the Agent may not work on (StudentDesktopCheck logs that once).
            if (loggedOn is not { Desktop: { } desktop } student
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

    private void Report(string outcome)
    {
        if (outcome != _lastOutcome)
        {
            _lastOutcome = outcome;
            _log(outcome);
        }
    }

    private void ReportNames(string outcome)
    {
        if (outcome != _lastNamesOutcome)
        {
            _lastNamesOutcome = outcome;
            _log(outcome);
        }
    }
}
