using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;
using Midview.Protocol;

namespace Midview.Master;

// RemoteClassFolderController with the home's Master closed (plan decision 9,
// Review Focus 4): Lesson is the newest lesson the room's online Agents follow,
// and "Encerrar a aula", keeping the files, ends it straight on every Agent as
// the same lesson ended, newer than any of theirs. Any trusted teacher may end
// a lesson on the Agents; the home, opened again, finds the end on them and
// takes it as one nobody collected (ClassFolderSync.AdoptAgentsEndAsync), so
// what the PCs set aside comes in at its next lesson.
public sealed partial class RemoteClassFolderController
{
    // How often, with the home closed, the Agents are asked which lesson they follow.
    private static readonly TimeSpan AgentsInterval = TimeSpan.FromSeconds(10);

    // The newest lesson the Agents follow, and when they were last asked (null:
    // not since the home closed). Under _lock.
    private ClassLesson? _agentsLesson;
    private long? _agentsReadAt;

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
        var newest = Newest(await AgentsLessonsAsync().ConfigureAwait(false));
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

    // Only the lesson on, of that class, on the Agents that answer: refused
    // otherwise, as an end is on the home. What each PC of the classroom
    // answered, as when the home ends a lesson.
    private async Task<IReadOnlyList<ResultLineDto>> EndOnTheAgentsAsync(string turma)
    {
        var before = Snapshot();
        var newest = Newest(await AgentsLessonsAsync().ConfigureAwait(false));
        if (newest is null)
        {
            throw new ClassFolderRefusedException(ClassFolderText.EndFailed(turma, ClassFolderText.NoPcAnswered));
        }

        IReadOnlyList<ResultLineDto> lines = Array.Empty<ResultLineDto>();
        var on = newest.IsOn && string.Equals(newest.Turma, turma, StringComparison.OrdinalIgnoreCase);
        var lesson = newest;
        if (on)
        {
            var savedAt = Math.Max(UnixNow(), newest.SavedAt == long.MaxValue ? long.MaxValue : newest.SavedAt + 1);
            var ended = newest with { SavedAt = savedAt, Ended = true };
            lines = await Task.WhenAll(Machines().Select(machine => Task.Run(() => SendLesson(machine, ended)))).ConfigureAwait(false);
            if (lines.Any(line => line.Ok))
            {
                lesson = ended;
            }
        }

        lock (_lock)
        {
            _agentsReadAt = Environment.TickCount64;
            KeepAgentsLesson(lesson);
        }
        if (!before.IsSameAs(Snapshot()))
        {
            RaiseChanged();
        }
        return on ? lines : throw new ClassFolderRefusedException(ClassFolderText.LessonAlreadyOver(turma));
    }

    // The newest by SavedAt; null for none.
    private static ClassLesson? Newest(IEnumerable<ClassLesson> lessons) => lessons.MaxBy(lesson => lesson.SavedAt);

    // What the online Agents follow (those that answer, and know the class folder).
    private async Task<IReadOnlyList<ClassLesson>> AgentsLessonsAsync()
    {
        var online = Machines().Where(machine => machine.AgentThumbprint is { Length: > 0 } && IsOnline(machine)).ToList();
        var lessons = await Task.WhenAll(online.Select(machine => Task.Run(() => LessonOf(machine)))).ConfigureAwait(false);
        return lessons.OfType<ClassLesson>().ToList();
    }

    private ClassLesson? LessonOf(KnownMachine machine)
    {
        try
        {
            return ClassFolderClient.GetLesson(machine, _certificate, _agentPort).Lesson;
        }
        catch (Exception ex)
        {
            LogUnexpected(ex, $"Reading the lesson of {machine.Name} failed");
            return null;
        }
    }

    // As ClassFolderSync sends a lesson: an Agent that keeps a newer one says
    // yes, saying so, and that is no.
    private ResultLineDto SendLesson(KnownMachine machine, ClassLesson lesson)
    {
        try
        {
            var (succeeded, detail) = ClassFolderClient.SetLesson(machine, _certificate, lesson, _agentPort);
            if (succeeded && detail.StartsWith(ClassLesson.KeptNewerText, StringComparison.Ordinal))
            {
                succeeded = false;
            }
            return new ResultLineDto(machine.Name, succeeded, detail);
        }
        catch (Exception ex)
        {
            return new ResultLineDto(machine.Name, false, FailureText.DescribeAgent(ex));
        }
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

    // A machine off, unreachable or not trusting this teacher is no news.
    private void LogUnexpected(Exception ex, string what)
    {
        if (ex is not (SocketException or IOException or AuthenticationException or OperationCanceledException))
        {
            _log($"{what}: {ex}");
        }
    }
}
