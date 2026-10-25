using Midview.Protocol;

namespace Midview.Master;

// RemoteClassFolderController with the home's Master closed (plan decision 9,
// Review Focus 4): Lesson is the newest lesson the room's online Agents follow
// (as the home itself counts them: ClassLessonOnAgents), and "Encerrar a
// aula", keeping the files, ends it straight on every Agent as the same lesson
// ended, newer than any of theirs. Any trusted teacher may end a lesson on the
// Agents; the home, opened again, finds the end on them and takes it as one
// nobody collected (ClassFolderSync.AdoptAgentsEndAsync), so what the PCs set
// aside comes in at its next lesson. Until the home's own state has that end,
// this PC shows its end rather than the home's older word on that lesson.
public sealed partial class RemoteClassFolderController
{
    // How often, with the home closed, the Agents are asked which lesson they follow.
    private static readonly TimeSpan AgentsInterval = TimeSpan.FromSeconds(10);

    // The newest lesson the Agents follow, and when they were last asked (null:
    // not since the home closed). Under _lock.
    private ClassLesson? _agentsLesson;
    private long? _agentsReadAt;

    // The lesson this PC ended on the Agents with the home closed. Under _lock.
    private ClassLesson? _endedHere;

    private bool AgentsLessonDue()
    {
        lock (_lock)
        {
            return _closed && (_agentsReadAt is not { } at || Environment.TickCount64 - at >= AgentsInterval.TotalMilliseconds);
        }
    }

    // Kept as Lesson while the home is closed; the last one stays when no Agent answers.
    private async Task ReadAgentsLessonAsync()
    {
        var newest = await AgentsNewestAsync().ConfigureAwait(false);
        lock (_lock)
        {
            _agentsReadAt = Environment.TickCount64;
            KeepAgentsLesson(newest);
        }
    }

    // Only a newer lesson replaces the one kept, as on the Agents: a read that
    // went out before this PC ended the lesson on them doesn't bring it back on.
    private void KeepAgentsLesson(ClassLesson? lesson)
    {
        if (lesson is not null && (_agentsLesson is not { } kept || lesson.SavedAt >= kept.SavedAt))
        {
            _agentsLesson = lesson;
        }
    }

    // The home's lesson, unless this PC ended that very lesson on the Agents
    // later than the home's word on it (the home, back, hasn't taken the end in yet).
    private ClassLesson HomesLessonLocked()
    {
        var home = _state?.Lesson.ToLesson() ?? ClassLesson.None(0);
        return _endedHere is { } ended && ended.LessonId == home.LessonId && ended.SavedAt > home.SavedAt ? ended : home;
    }

    // Only the lesson on, of that class, on the Agents that answer: refused
    // otherwise, as an end is on the home. What each PC of the classroom
    // answered, as when the home ends a lesson; a PC the grid shows off isn't
    // tried (FailureText.Offline): nothing hands it the end until the home's
    // Master opens, or the day ends.
    private async Task<IReadOnlyList<ResultLineDto>> EndOnTheAgentsAsync(string turma)
    {
        var before = Snapshot();
        var newest = await AgentsNewestAsync().ConfigureAwait(false);
        if (newest is null)
        {
            throw new ClassFolderRefusedException(ClassFolderText.EndFailed(turma, ClassFolderText.NoPcAnswered));
        }

        IReadOnlyList<ResultLineDto> lines = Array.Empty<ResultLineDto>();
        var on = newest.IsOn && string.Equals(newest.Turma, turma, StringComparison.OrdinalIgnoreCase);
        ClassLesson? endedIt = null;
        if (on)
        {
            var savedAt = Math.Max(UnixNow(), newest.SavedAt == long.MaxValue ? long.MaxValue : newest.SavedAt + 1);
            var ended = newest with { SavedAt = savedAt, Ended = true };
            lines = await Task.WhenAll(Machines().Select(machine => OwnThread(() => SendEnd(machine, ended)))).ConfigureAwait(false);
            if (lines.Any(line => line.Ok))
            {
                endedIt = ended;
            }
        }

        lock (_lock)
        {
            _agentsReadAt = Environment.TickCount64;
            KeepAgentsLesson(endedIt ?? newest);
            _endedHere = endedIt ?? _endedHere;
        }
        if (!before.IsSameAs(Snapshot()))
        {
            RaiseChanged();
        }
        return on ? lines : throw new ClassFolderRefusedException(ClassFolderText.LessonAlreadyOver(turma));
    }

    private ResultLineDto SendEnd(KnownMachine machine, ClassLesson ended)
    {
        if (!IsOnline(machine))
        {
            return new ResultLineDto(machine.Name, false, FailureText.Offline);
        }
        var (ok, detail) = ClassLessonOnAgents.Send(machine, _certificate, ended, _agentPort);
        return new ResultLineDto(machine.Name, ok, detail);
    }

    // The newest lesson the online Agents follow (those that answer and know
    // the class folder), as the home counts it; null for none.
    private async Task<ClassLesson?> AgentsNewestAsync()
    {
        var online = Machines().Where(machine => machine.AgentThumbprint is { Length: > 0 } && IsOnline(machine)).ToList();
        var lessons = await Task.WhenAll(online.Select(machine => OwnThread(
            () => ClassLessonOnAgents.Read(machine, _certificate, _agentPort, _log)))).ConfigureAwait(false);
        return ClassLessonOnAgents.Newest(lessons, UnixNow());
    }

    private IReadOnlyList<KnownMachine> Machines()
    {
        try
        {
            return _machines();
        }
        catch (Exception ex)
        {
            _log($"Reading the classroom list failed: {ex}");
            return Array.Empty<KnownMachine>();
        }
    }

    // A machine whose state can't be told is taken for off.
    private bool IsOnline(KnownMachine machine)
    {
        try
        {
            return _isOnline(machine);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private long UnixNow() => new DateTimeOffset(_localNow()).ToUnixTimeSeconds();
}
