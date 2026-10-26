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
// the policy goes, if it is Midview's (a class folder: a download folder set
// by someone else stays, DownloadFolderPolicy.Remove). A lesson that started
// on an earlier day ends by itself. An open Chrome keeps saving to the old
// folder until it is started again, so it is closed whenever the folder it
// saves into changes, and only then.
//
// As SYSTEM on the student's folders: nothing inside a folder is ever
// touched (the Session, as the student, moves the files), and a Desktop or
// folder that is a link is left alone, not followed.
//
// An end ends only the lesson it is of: with two teacher PCs in one lab, the
// end of the other PC's lesson (one it held, a start at the same time) must
// not take this PC out of the home's lesson, which nobody would hand it again.
//
// In two files: this one (the lesson) and ClassLessonHandler.Folders.cs (the
// student's Desktop and download folder made to match it).
public sealed partial class ClassLessonHandler
{
    // The start of every class folder's name (U+2013 EN DASH), as
    // ClassLesson.FolderName makes it.
    internal const string FolderPrefix = "Pasta da turma – ";

    // How long a claim on the class folders' home, or a lesson set, waits for
    // the lesson's lock (Rulings 18, 19 and 22): ClassHomeClaim.MaxLockWait,
    // which the Master waits out before taking a claim it got no answer to as refused.
    public static readonly TimeSpan DefaultClaimWait = ClassHomeClaim.MaxLockWait;

    private readonly string _statePath;
    private readonly Func<(string? Desktop, SecurityIdentifier Student)?> _loggedOnStudent;
    private readonly Func<SecurityIdentifier, RegistryKey?> _openUserSoftware;
    private readonly AppBlockEnforcer _enforcer;
    private readonly SecurityIdentifier _folderOwner;
    private readonly Func<DateTime> _localNow;
    private readonly Action<string> _log;
    private readonly ClassHomeState? _home;
    private readonly TimeSpan _claimWait;
    private readonly object _gate = new();
    private ClassLesson _lesson;

    // The first look after the Agent starts is the startup one, whichever
    // comes first (the service's first poll or ReconcileAtStartup).
    private bool _lookedOnce;
    private string? _lastOutcome;

    // loggedOnStudent: the student at the console and their Desktop (null
    // when there is none the Agent may work on), or null when nobody is
    // logged on (UserSessionProcessLauncher.LoggedOnStudent). openUserSoftware: their
    // writable HKEY_USERS\<SID>\Software, null when not loaded
    // (DownloadFolderPolicy.OpenUserSoftware). folderOwner: SYSTEM outside
    // the tests. localNow: the local time, for a lesson from an earlier day.
    // home: the class folders' home PC, the only one that may start a lesson
    // or change the class (null: anyone may, as before two teacher PCs).
    // claimWait: how long a claim or a lesson set waits for the lesson's lock (DefaultClaimWait).
    public ClassLessonHandler(
        string statePath, Func<(string? Desktop, SecurityIdentifier Student)?> loggedOnStudent,
        Func<SecurityIdentifier, RegistryKey?> openUserSoftware, AppBlockEnforcer enforcer,
        SecurityIdentifier folderOwner, Func<DateTime> localNow, Action<string>? log = null,
        ClassHomeState? home = null, TimeSpan? claimWait = null)
    {
        _claimWait = claimWait ?? DefaultClaimWait;
        _statePath = statePath;
        _loggedOnStudent = loggedOnStudent;
        _openUserSoftware = openUserSoftware;
        _enforcer = enforcer;
        _folderOwner = folderOwner;
        _localNow = localNow;
        _log = log ?? AgentLog.Warn;
        _home = home;
        _lesson = Load(statePath);
    }

    // What this machine follows (GetClassLesson): what was last saved, even
    // when this PC's folder or policy couldn't be set up yet.
    public ClassLesson Current => Volatile.Read(ref _lesson);

    // Whether this machine keeps the class folders' home (GetClassHome and
    // ClaimClassHome), and which PC it is.
    public bool KeepsHome => _home is not null;

    public ClassHome? CurrentHome => _home?.Current;

    // A teacher PC saying it is the home, decided under the lesson's lock: a
    // start and a claim never cross, and the folders don't move to another
    // PC while a lesson is on. A look at the student's folders can hold that
    // lock a long time (closing the browsers, the registry): a claim that
    // can't have it within the claim wait is refused as busy
    // (ClassHomeClaim.BusyText) and is never applied later (Ruling 18), so a
    // Master that got no answer finds the home as it is when it asks again.
    public (bool Ok, string Detail) ClaimHome(string senderThumbprint, string senderAddress, ClassHomeClaim claim) =>
        _home is null
            ? (false, "esta máquina não guarda o PC das pastas")
            : Locked(() => _home.Claim(senderThumbprint, senderAddress, claim, lessonOn: _lesson.IsOn));

    // For the tests from before two teacher PCs (the Master's too): a sender
    // with no identity, which only a handler with no home lets start a lesson.
    public (bool Succeeded, string Detail) Handle(byte[] payload) => Handle(payload, "", "");

    // senderThumbprint: the TLS channel's; senderAddress: the connection's.
    // A start or a class change from a PC that isn't the home is refused
    // with nothing touched; an end is taken from any teacher, when it is of
    // the lesson on here or no lesson is on (one of another lesson changes
    // nothing, answered as an older lesson is). With no home,
    // the PC that starts a lesson becomes it. Under the lesson's lock, had
    // within the claim wait or refused as busy, as a claim (Ruling 22): the
    // Master's poller sends the lesson again, and this one is never applied later.
    public (bool Succeeded, string Detail) Handle(byte[] payload, string senderThumbprint, string senderAddress)
    {
        if (!ClassLesson.TryDecode(payload, out var lesson))
        {
            return (false, "pedido inválido");
        }
        if (lesson.IsOn && !IsFolderName(lesson.Turma))
        {
            return (false, "nome de turma inválido");
        }
        return Locked(() => Apply(lesson, senderThumbprint, senderAddress));
    }

    // Handle's work, with the lesson's lock held.
    private (bool Succeeded, string Detail) Apply(ClassLesson lesson, string senderThumbprint, string senderAddress)
    {
        if (lesson.IsOn && _home is not null && !_home.TryStart(senderThumbprint, senderAddress, out var refusal))
        {
            return (false, refusal);
        }
        if (lesson.SavedAt < _lesson.SavedAt || (!lesson.IsOn && _lesson.IsOn && lesson.LessonId != _lesson.LessonId))
        {
            return (true, $"{ClassLesson.KeptNewerText}: {Describe(_lesson)}");
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

    // work with the lesson's lock held; busy (ClassHomeClaim.BusyText) when it
    // can't be had within the claim wait (a look at the student's folders holds it).
    private (bool, string) Locked(Func<(bool, string)> work)
    {
        if (!Monitor.TryEnter(_gate, _claimWait))
        {
            return (false, ClassHomeClaim.BusyText);
        }
        try
        {
            return work();
        }
        finally
        {
            Monitor.Exit(_gate);
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
