using System.Security;
using System.Security.Principal;
using Microsoft.Win32;
using Midview.Protocol;

namespace Midview.Agent;

// The class folder's lesson on this machine ("Pasta da turma",
// MessageType.SetClassLesson and GetClassLesson): which turma is in the room,
// kept on disk (ClassLessonFile, which the Session reads too) until the
// teacher changes it. The newest SavedAt wins, as with the lab panel.
//
// While a lesson is on, the logged-on student's Desktop has the protected
// "Pasta da turma – <turma>" folder (ProtectedStudentFolders) and their
// Chrome and Edge download straight into it (DownloadFolderPolicy, in their
// own registry hive). Every other "Pasta da turma – " folder on that Desktop
// is given back to the student, and with no lesson on all of them are and
// the policy goes. A lesson that started on an earlier day ends by itself.
// An open Chrome keeps saving to the old folder until it is started again,
// so it is closed whenever the folder it saves into changes.
//
// As SYSTEM on the student's folders: nothing inside a folder is ever
// touched (the Session, as the student, moves the files), and a Desktop or
// folder that is a link is left alone, not followed.
public sealed class ClassLessonHandler
{
    // The start of every class folder's name (U+2013 EN DASH), as
    // ClassLesson.FolderName makes it.
    internal const string FolderPrefix = "Pasta da turma – ";

    private readonly string _statePath;
    private readonly Func<(string Desktop, SecurityIdentifier Student)?> _loggedOnStudent;
    private readonly Func<SecurityIdentifier, RegistryKey?> _openUserSoftware;
    private readonly AppBlockEnforcer _enforcer;
    private readonly SecurityIdentifier _folderOwner;
    private readonly Func<DateTime> _localNow;
    private readonly Action<string> _log;
    private readonly object _gate = new();
    private ClassLesson _lesson;

    // The class folder Protect last succeeded on (student, Desktop, name), so
    // later polls only check it; any other means Protect runs again. A folder
    // given back keeps its owner, so its owner alone can't say it is protected.
    private string? _protectedFor;

    // The class folders already given back (student and path), so a poll
    // doesn't give them back again.
    private readonly HashSet<string> _released = new(StringComparer.OrdinalIgnoreCase);

    // A policy write that failed may have left Chrome's value right and
    // Edge's not: the next look writes it again even if Chrome's matches.
    private bool _policyUnsure;

    // The first look after the Agent starts is the startup one, whichever
    // comes first (the service's first poll or ReconcileAtStartup).
    private bool _lookedOnce;
    private string? _lastOutcome;

    // loggedOnStudent: the student at the console and their Desktop, or null
    // (UserSessionProcessLauncher.LoggedOnStudent). openUserSoftware: their
    // writable HKEY_USERS\<SID>\Software, null when not loaded
    // (DownloadFolderPolicy.OpenUserSoftware). folderOwner: SYSTEM outside
    // the tests. localNow: the local time, for a lesson from an earlier day.
    public ClassLessonHandler(
        string statePath, Func<(string Desktop, SecurityIdentifier Student)?> loggedOnStudent,
        Func<SecurityIdentifier, RegistryKey?> openUserSoftware, AppBlockEnforcer enforcer,
        SecurityIdentifier folderOwner, Func<DateTime> localNow, Action<string>? log = null)
    {
        _statePath = statePath;
        _loggedOnStudent = loggedOnStudent;
        _openUserSoftware = openUserSoftware;
        _enforcer = enforcer;
        _folderOwner = folderOwner;
        _localNow = localNow;
        _log = log ?? AgentLog.Warn;
        _lesson = Load(statePath);
    }

    // What this machine follows (GetClassLesson): what was last saved, even
    // when this PC's folder or policy couldn't be set up yet.
    public ClassLesson Current => Volatile.Read(ref _lesson);

    public (bool Succeeded, string Detail) Handle(byte[] payload)
    {
        if (!ClassLesson.TryDecode(payload, out var lesson))
        {
            return (false, "pedido inválido");
        }
        if (lesson.IsOn && !IsFolderName(lesson.Turma))
        {
            return (false, "nome de turma inválido");
        }

        lock (_gate)
        {
            if (lesson.SavedAt < _lesson.SavedAt)
            {
                return (true, $"mantida a aula mais nova: {Describe(_lesson)}");
            }
            if (lesson.SavedAt == _lesson.SavedAt && lesson != _lesson)
            {
                return (false, "outra aula foi salva no mesmo segundo; envie de novo");
            }

            if (lesson != _lesson)
            {
                try
                {
                    ClassLessonFile.Write(_statePath, lesson);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _log($"Couldn't save the class lesson: {ex.Message}");
                    return (false, $"não foi possível gravar a aula: {ErrorText.Describe(ex)}");
                }
                Volatile.Write(ref _lesson, lesson);
            }

            // The teacher's own command: Chrome is closed if its folder changed.
            var problems = ReconcileLocked(mayCloseBrowsers: true);
            var detail = Describe(_lesson);
            if (problems.Count == 0)
            {
                return (true, detail);
            }
            var what = _lesson.IsOn ? "preparar" : "liberar";
            return (true, $"{detail}; não foi possível {what} a pasta agora: {string.Join("; ", problems)}");
        }
    }

    // On every poll. Idempotent, and never throws.
    public void Reconcile()
    {
        lock (_gate)
        {
            ReconcileLocked(mayCloseBrowsers: _lookedOnce);
        }
    }

    // At the Agent's start: the same, but the student's Chrome isn't closed.
    public void ReconcileAtStartup()
    {
        lock (_gate)
        {
            ReconcileLocked(mayCloseBrowsers: false);
        }
    }

    public static string Describe(ClassLesson lesson) => lesson.IsOn ? $"aula do {lesson.Turma}" : "aula encerrada";

    // A turma is only ever a plain folder name (what the Master's class list
    // makes): one FileNames.Safe leaves as it is, so no trailing dot or space
    // nor a character Windows refuses. Never "${", which Chrome would expand
    // as a policy variable in the download folder.
    private static bool IsFolderName(string turma) =>
        turma.Length > 0
        && turma.Length <= ClassLesson.MaxTurmaLength
        && FileNames.Safe(turma) == turma
        && !turma.Contains("${", StringComparison.Ordinal);

    // Problems, in Portuguese, for the teacher; each new outcome is logged once.
    private List<string> ReconcileLocked(bool mayCloseBrowsers)
    {
        _lookedOnce = true;
        var done = new List<string>();
        var problems = new List<string>();
        try
        {
            EndIfFromAnEarlierDay(done, problems);
            var lesson = _lesson;
            if (_loggedOnStudent() is not { } student)
            {
                // At the next logon everything is looked at again.
                _protectedFor = null;
                _released.Clear();
            }
            else if (FollowLesson(lesson, student.Desktop, student.Student, done, problems) && mayCloseBrowsers)
            {
                _enforcer.EndNow("chrome.exe", LabConfigCommandHandler.ChromeClosedNotice);
                _enforcer.EndNow("msedge.exe");
                done.Add("closed the student's Chrome and Edge");
            }
        }
        catch (Exception ex)
        {
            problems.Add(ex.Message);
        }

        Report(done.Count + problems.Count == 0 ? null : $"Class lesson ({Describe(_lesson)}): {string.Join("; ", done.Concat(problems))}");
        return problems;
    }

    // Kept on file before anything else: the Session goes by the file. If it
    // can't be saved, the lesson stays on and the next look tries again.
    private void EndIfFromAnEarlierDay(List<string> done, List<string> problems)
    {
        var lesson = _lesson;
        if (!lesson.IsOn || !lesson.StartedBefore(_localNow()))
        {
            return;
        }

        // One second newer, so the Master resending the old lesson doesn't
        // start it again.
        var ended = lesson with { SavedAt = lesson.SavedAt == long.MaxValue ? long.MaxValue : lesson.SavedAt + 1, Ended = true };
        try
        {
            ClassLessonFile.Write(_statePath, ended);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"a aula de um dia anterior não pôde ser encerrada: {ErrorText.Describe(ex)}");
            return;
        }
        Volatile.Write(ref _lesson, ended);
        done.Add($"ended the lesson of {lesson.Turma}, which started on an earlier day");
    }

    // The logged-on student's Desktop and download folder made to match the
    // lesson. True when the folder Chrome saves into changed.
    private bool FollowLesson(
        ClassLesson lesson, string desktop, SecurityIdentifier student, List<string> done, List<string> problems)
    {
        // Opened as itself: a Desktop that is missing or a link (redirected
        // elsewhere) gets nothing, not even the download folder. With no
        // lesson on there was nothing to put there either.
        var desktopUsable = ProtectedStudentFolders.IsRealDirectory(desktop);
        if (!desktopUsable)
        {
            if (lesson.IsOn)
            {
                problems.Add($"Área de Trabalho não encontrada ou não é uma pasta comum ({desktop})");
            }
        }
        else
        {
            if (lesson.IsOn)
            {
                ProtectClassFolder(lesson, desktop, student, done, problems);
            }
            else
            {
                _protectedFor = null;
            }
            ReleaseOtherClassFolders(lesson, desktop, student, done, problems);
        }

        if (!lesson.IsOn)
        {
            return PointDownloads(student, null, done, problems);
        }
        return desktopUsable && PointDownloads(student, Path.Combine(desktop, lesson.FolderName), done, problems);
    }

    private void ProtectClassFolder(
        ClassLesson lesson, string desktop, SecurityIdentifier student, List<string> done, List<string> problems)
    {
        var names = new[] { lesson.FolderName };
        var folder = Path.Combine(desktop, lesson.FolderName);
        var key = $"{student.Value}|{folder}";
        _released.Remove(key);
        if (string.Equals(_protectedFor, key, StringComparison.OrdinalIgnoreCase)
            && !ProtectedStudentFolders.NeedsProtecting(desktop, _folderOwner, names))
        {
            return;
        }

        var folderProblems = ProtectedStudentFolders.Protect(desktop, student, _folderOwner, names);
        if (folderProblems.Count == 0)
        {
            _protectedFor = key;
            done.Add($"protected {folder}");
        }
        else
        {
            _protectedFor = null;
            problems.AddRange(folderProblems);
        }
    }

    // Every class folder on the Desktop but the lesson's own is given back
    // (once each). A link of that name is left alone.
    private void ReleaseOtherClassFolders(
        ClassLesson lesson, string desktop, SecurityIdentifier student, List<string> done, List<string> problems)
    {
        List<string> names;
        try
        {
            names = new DirectoryInfo(desktop).EnumerateDirectories()
                .Where(folder => folder.Name.StartsWith(FolderPrefix, StringComparison.OrdinalIgnoreCase)
                    && !(lesson.IsOn && string.Equals(folder.Name, lesson.FolderName, StringComparison.OrdinalIgnoreCase))
                    && (folder.Attributes & FileAttributes.ReparsePoint) == 0
                    && !_released.Contains($"{student.Value}|{Path.Combine(desktop, folder.Name)}"))
                .Select(folder => folder.Name)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            problems.Add($"Área de Trabalho ({desktop}): {ErrorText.Describe(ex)}");
            return;
        }

        foreach (var name in names)
        {
            var folderProblems = ProtectedStudentFolders.Release(desktop, student, new[] { name });
            if (folderProblems.Count == 0)
            {
                _released.Add($"{student.Value}|{Path.Combine(desktop, name)}");
                done.Add($"gave {name} back to the student");
            }
            else
            {
                problems.AddRange(folderProblems);
            }
        }
    }

    // Chrome's and Edge's download folder set to folder (null: removed).
    // True when Chrome's changed.
    private bool PointDownloads(SecurityIdentifier student, string? folder, List<string> done, List<string> problems)
    {
        try
        {
            using var software = _openUserSoftware(student);
            if (software is null)
            {
                problems.Add("o registro do aluno não está carregado; a pasta de downloads do navegador fica para depois");
                return false;
            }

            var before = DownloadFolderPolicy.Current(software);
            if (before == folder && !_policyUnsure)
            {
                return false;
            }

            Exception? failure = null;
            try
            {
                if (folder is null)
                {
                    DownloadFolderPolicy.Remove(software);
                }
                else
                {
                    DownloadFolderPolicy.Apply(software, folder);
                }
            }
            catch (Exception ex) when (IsRegistryError(ex))
            {
                failure = ex;
            }
            _policyUnsure = failure is not null;

            var after = DownloadFolderPolicy.Current(software);
            if (failure is not null)
            {
                problems.Add($"pasta de downloads do navegador: {ErrorText.Describe(failure)}");
            }
            if (after == before)
            {
                return false;
            }
            done.Add(after is null ? "removed the browsers' download folder" : $"pointed the browsers' downloads at {after}");
            return true;
        }
        catch (Exception ex) when (IsRegistryError(ex))
        {
            _policyUnsure = true;
            problems.Add($"pasta de downloads do navegador: {ErrorText.Describe(ex)}");
            return false;
        }
    }

    private static bool IsRegistryError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or SecurityException;

    private void Report(string? outcome)
    {
        if (outcome != _lastOutcome)
        {
            _lastOutcome = outcome;
            if (outcome is not null)
            {
                _log(outcome);
            }
        }
    }

    // Unreadable means no lesson: the side the teacher can always fix
    // remotely. A corrupt file is set aside; one that can't be opened right
    // now is left where it is.
    private ClassLesson Load(string path)
    {
        if (!File.Exists(path))
        {
            return ClassLesson.None(0);
        }

        try
        {
            File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"Couldn't read the class lesson file, following no lesson: {ex.Message}");
            return ClassLesson.None(0);
        }

        if (ClassLessonFile.Read(path) is { } lesson)
        {
            return lesson;
        }

        _log("The class lesson file is corrupt, following no lesson; it is kept aside as .corrupt.");
        try
        {
            File.Move(path, path + ".corrupt", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left in place; read again (and failing again) next start.
        }
        return ClassLesson.None(0);
    }
}
