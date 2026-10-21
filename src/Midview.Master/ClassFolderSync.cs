using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// How one PC stands with the class folder, for the "Pasta da turma" window.
public enum PcSyncState
{
    // Its folder is the class's.
    InSync,

    // Files are on their way, or the PC's Session hasn't taken up the lesson yet.
    Copying,

    // Not reachable now.
    Offline,

    // An old Midview, no folder, a refusal, or a file that couldn't be copied
    // (tried again every round). Detail says why, in the teacher's words.
    Failed,
}

public sealed record PcStatus(KnownMachine Machine, PcSyncState State, string Detail);

// The teacher's side of the class folder ("Pasta da turma",
// docs/superpowers/specs/2026-10-20-class-folder-design.md: "Synchronisation",
// "Changes and conflicts", "Ending a lesson", "A lesson nobody ended"). It starts
// and ends the lesson on every PC's Agent and, while a lesson is on, keeps every
// online PC's class folder the same as the class's store (ClassStore), the copy
// that counts: every interval each online PC is listed, ClassSyncPlanner says
// what each file needs, and the files are pulled, pushed, renamed or deleted
// (ClassFolderClient), at most 4 PCs copying at once and never two rounds of one
// PC at once. What a PC set aside for a lesson that is over is brought into that
// lesson's class, unless "Encerrar a aula" collected that lesson already.
//
// The lesson is kept in a file (ClassLessonFileMaster), so a Master closed and
// opened again carries on with it; one that started on an earlier day is ended,
// keeping the files, as soon as it is seen. Every lesson change has a SavedAt
// newer than the last one (an Agent refuses a different lesson saved in the
// same second).
//
// machines and isOnline are called from background threads, and the events are
// raised on them. Nothing a PC does (failing, going away, answering nonsense)
// escapes as an exception: it becomes that PC's status.
//
// In five files: this one (the lesson), ClassFolderSync.Loop.cs (the loop and
// its rounds), ClassFolderSync.Round.cs (one PC's round), ClassFolderSync.Copy.cs
// (each file's pull, push, rename or deletion) and ClassFolderSync.SetAside.cs
// (what PCs set aside for lessons that are over).
public sealed partial class ClassFolderSync : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);

    // As Enviar and Recolher.
    private const int MaxCopyingPcs = 4;

    // Copies, deletions and renames in one PC's round; the rest waits for the
    // next, re-planned from a fresh listing (a lesson's last rounds have no limit).
    internal const int MaxActionsPerRound = 25;

    // How long Dispose waits for rounds in the middle of a copy (a network wait is up to 30 s).
    private static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(10);

    // Before the second of a lesson's last two rounds, so a file first seen in
    // the first one has had a moment to show it holds still.
    private static readonly TimeSpan EndSettle = TimeSpan.FromSeconds(1);

    private readonly ClassStore _store;
    private readonly X509Certificate2 _masterCertificate;
    private readonly Func<IReadOnlyList<KnownMachine>> _machines;
    private readonly Func<KnownMachine, bool> _isOnline;
    private readonly Func<DateTime> _localNow;
    private readonly int _agentPort;
    private readonly int _sessionPort;
    private readonly TimeSpan _interval;
    private readonly string _lessonPath;
    private readonly Action<string> _log;

    // One start or end at a time.
    private readonly SemaphoreSlim _lessonGate = new(1, 1);
    private readonly SemaphoreSlim _copying = new(MaxCopyingPcs, MaxCopyingPcs);

    // By machine key (the Session's thumbprint, as the store's per-PC records).
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _pcGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task> _loopRounds = new(StringComparer.OrdinalIgnoreCase);

    // Every round but a lesson end's own last ones, from before it starts until it is over (TrackedAsync).
    private readonly ConcurrentDictionary<long, Task> _rounds = new();
    private long _lastRoundId;
    private readonly ConcurrentDictionary<string, PcStatus> _statuses = new(StringComparer.OrdinalIgnoreCase);

    // The SavedAt of the lesson each Agent took.
    private readonly ConcurrentDictionary<string, long> _delivered = new(StringComparer.OrdinalIgnoreCase);

    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private ClassLesson _lesson;
    private volatile bool _changingLesson;
    private int _disposed;

    // interval: between two listings of a PC (DefaultInterval). lessonPath: the
    // lesson's file (ClassLessonFileMaster.Path). log: what went wrong that no
    // status says, in English, by default to System.Diagnostics.Trace.
    public ClassFolderSync(
        ClassStore store, X509Certificate2 masterCertificate, Func<IReadOnlyList<KnownMachine>> machines,
        Func<KnownMachine, bool> isOnline, Func<DateTime> localNow, int agentPort = ConnectionClient.AgentPort,
        int sessionPort = ConnectionClient.SessionPort, TimeSpan? interval = null, string? lessonPath = null,
        Action<string>? log = null)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        _store = store;
        _masterCertificate = masterCertificate;
        _machines = machines;
        _isOnline = isOnline;
        _localNow = localNow;
        _agentPort = agentPort;
        _sessionPort = sessionPort;
        _interval = interval ?? DefaultInterval;
        _lessonPath = lessonPath ?? ClassLessonFileMaster.Path;
        _log = log ?? (message => System.Diagnostics.Trace.WriteLine("ClassFolderSync: " + message));

        _lesson = ClassLessonFileMaster.Read(_lessonPath) ?? ClassLesson.None(0);
        if (_lesson.IsOn)
        {
            if (!ClassStore.IsValidName(_lesson.Turma) || _lesson.StartedBefore(_localNow()))
            {
                EndNobodyEnded(_lesson);
            }
            else
            {
                _store.RememberLesson(_lesson.LessonId, _lesson.Turma);
            }
        }
        _loop = Task.Run(LoopAsync);
    }

    // Raised (on a background thread) when a PC's state or its detail changes.
    public event Action<PcStatus>? StatusChanged;

    // Raised (on a background thread) when a lesson starts or ends.
    public event Action<ClassLesson>? LessonChanged;

    // The lesson on now, or the last one, ended (ClassLesson.None(0) before any).
    public ClassLesson Lesson => Volatile.Read(ref _lesson);

    // The last state of each PC seen in this lesson.
    public IReadOnlyList<PcStatus> Statuses => _statuses.Values.ToList();

    // What the last EndAsync couldn't do with the class's files (one line each, for the teacher).
    public IReadOnlyList<string> LastEndProblems { get; private set; } = Array.Empty<string>();

    // For the tests: called as a round with copying to do starts waiting for its turn.
    internal Action<KnownMachine>? WaitingForCopySlot { get; set; }

    // A new lesson of that class: saved, then sent to every PC's Agent at once,
    // with what each answered. The window only starts one with no lesson on
    // ("Trocar de turma…" ends the lesson first, with EndAsync). A lesson still
    // on is ended first, whatever its class, as a lesson nobody ended (keeping
    // the files, without its last rounds, every version known of them
    // remembered): what its PCs set aside comes in over the next rounds, only
    // what was never seen. Throws ArgumentException for a class that isn't there.
    public async Task<IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)>> StartAsync(string turma)
    {
        if (!_store.Exists(turma))
        {
            throw new ArgumentException("Essa turma não existe mais.", nameof(turma));
        }

        await _lessonGate.WaitAsync(_stop.Token).ConfigureAwait(false);
        try
        {
            _changingLesson = true;
            await RunningRounds().ConfigureAwait(false);
            var previous = Lesson;
            if (previous.IsOn)
            {
                EndUncollected(previous);
            }

            var lesson = new ClassLesson(NextSavedAt(), Guid.NewGuid(), turma, UnixNow(), Ended: false);
            _store.RememberLesson(lesson.LessonId, turma);
            Adopt(lesson);
            return await SendToEveryAgentAsync(lesson).ConfigureAwait(false);
        }
        finally
        {
            _changingLesson = false;
            _lessonGate.Release();
        }
    }

    // "Encerrar a aula": two last rounds with every online PC (bringing files in
    // only, the second for files first seen in the first), the lesson marked
    // collected, the ended lesson saved, the teacher's choice applied to the
    // class's files (LastEndProblems says what couldn't be), and the ended lesson
    // sent to every Agent, with what each answered. Nothing when no lesson is on.
    //
    // The ended lesson is saved before the choice is applied: a Master that stops
    // in between keeps files it should have deleted, rather than finding the lesson
    // on with the per-PC records gone and pulling the deleted files back in.
    public async Task<IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)>> EndAsync(EndChoice choice)
    {
        if (!Enum.IsDefined(choice))
        {
            throw new ArgumentOutOfRangeException(nameof(choice));
        }

        var token = _stop.Token;
        await _lessonGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var lesson = Lesson;
            if (!lesson.IsOn)
            {
                return Array.Empty<(KnownMachine, bool, string)>();
            }

            _changingLesson = true;
            await RunningRounds().ConfigureAwait(false);
            await LastRoundsAsync(lesson, token).ConfigureAwait(false);

            _store.MarkCollected(lesson.LessonId);
            var ended = lesson with { SavedAt = NextSavedAt(), Ended = true };
            Adopt(ended);
            LastEndProblems = EndInStore(lesson.Turma, choice);
            return await SendToEveryAgentAsync(ended).ConfigureAwait(false);
        }
        finally
        {
            _changingLesson = false;
            _lessonGate.Release();
        }
    }

    // One round with that PC now (the loop runs them by itself); waits for one
    // already running. Nothing when no lesson is on.
    public async Task RunRoundAsync(KnownMachine machine, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        var lesson = Lesson;
        await TrackedAsync(() => GatedRoundAsync(machine, lesson, collectOnly: false, linked.Token)).ConfigureAwait(false);
    }

    // Stops the loop and waits a while for the rounds in progress.
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stop.Cancel();
        try
        {
            _loop.Wait(DisposeWait);
        }
        catch (AggregateException)
        {
            // The loop catches everything; nothing to report here.
        }
    }

    // --- The lesson ---

    private long UnixNow() => new DateTimeOffset(_localNow()).ToUnixTimeSeconds();

    private long NextSavedAt()
    {
        var last = Lesson.SavedAt;
        return last == long.MaxValue ? long.MaxValue : Math.Max(UnixNow(), last + 1);
    }

    // Saved (a failure is only logged: the lesson goes on in memory), made the
    // current one, and told.
    private void Adopt(ClassLesson lesson)
    {
        try
        {
            ClassLessonFileMaster.Write(lesson, _lessonPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"The lesson could not be saved: {ex.Message}");
        }

        Volatile.Write(ref _lesson, lesson);
        _listings.Clear();
        _delivered.Clear();
        _statuses.Clear();
        try
        {
            LessonChanged?.Invoke(lesson);
        }
        catch (Exception ex)
        {
            _log($"A LessonChanged handler failed: {ex}");
        }
    }

    // "A lesson nobody ended" ends at the end of its day, keeping the files. Not
    // collected: what the PCs set aside for it still comes in. The Agents end it
    // by themselves; the pollers hand them this ended one too.
    private void EndNobodyEnded(ClassLesson lesson)
    {
        EndUncollected(lesson);
        Adopt(lesson with { SavedAt = NextSavedAt(), Ended = true });
    }

    // A lesson that ends without its last rounds, keeping the files: what its
    // class held and what each PC was known to have are remembered first (the
    // end clears them), so of what the PCs then set aside only versions never
    // seen come in, and a file the teacher deleted doesn't come back.
    private void EndUncollected(ClassLesson lesson)
    {
        if (ClassStore.IsValidName(lesson.Turma))
        {
            try
            {
                _store.RememberVersionsOf(lesson.LessonId, lesson.Turma);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log($"The versions of {lesson.Turma} could not be remembered: {ex.Message}");
            }
        }
        EndInStore(lesson.Turma, EndChoice.Keep);
    }

    private IReadOnlyList<string> EndInStore(string turma, EndChoice choice)
    {
        try
        {
            return _store.EndLesson(turma, choice).Problems;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log($"Ending the lesson of {turma} in the store failed: {ex.Message}");
            return new[] { ErrorText.Describe(ex) };
        }
    }

    private async Task<IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)>> SendToEveryAgentAsync(ClassLesson lesson)
    {
        var machines = Machines();
        return await Task.WhenAll(machines.Select(machine => Task.Run(() => SendLesson(machine, lesson)))).ConfigureAwait(false);
    }

    private (KnownMachine Machine, bool Ok, string Detail) SendLesson(KnownMachine machine, ClassLesson lesson)
    {
        try
        {
            var (succeeded, detail) = ClassFolderClient.SetLesson(machine, _masterCertificate, lesson, _agentPort);
            if (succeeded)
            {
                _delivered[Key(machine)] = lesson.SavedAt;
            }
            return (machine, succeeded, detail);
        }
        catch (Exception ex)
        {
            return (machine, false, FailureText.DescribeAgent(ex));
        }
    }
}
