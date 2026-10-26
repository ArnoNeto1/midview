using System.IO;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderSync: a lesson of this PC's that another home's PCs left behind
// or that this PC holds for the room (with two teacher PCs in one lab), and
// the lessons this PC knows are over.
//
// The end of a lesson this PC holds while another PC is, or may be, the home
// goes only to the Agents still in it, saved one second after the lesson.
// An Agent with no lesson on takes any newer end, so an end saved now would
// leave it holding something newer than the other home's lesson, started
// since: that lesson would then be refused there, and the room stay split.
public sealed partial class ClassFolderSync
{
    // The lesson here is the end of a lesson this PC held for the room while
    // another PC is, or may be, the home (EndAsync): the pollers hand it only
    // to an Agent still in that lesson.
    private ClassLesson? _heldEnd;

    public bool EndGoesOnlyToItsLesson => Volatile.Read(ref _heldEnd) is { } ended && ended == Lesson;

    // For a PC whose room keeps another PC as the home, as ClassFolderRole
    // confirmed it: StandDownIfNoLessonAsync, except for a lesson on here that
    // the room's online Agents left behind (the newest lesson they follow is
    // this one ended, or another one saved later: the new home's), which is
    // first ended, as one nobody collected (no last rounds, the files kept,
    // every version known of them remembered), as AdoptAgentsEndAsync ends one
    // another PC ended: a stale lesson must not keep this PC from taking the
    // folders over until the day ends. Its end goes first to the Agents still
    // in it (SendHeldEndAsync), which would otherwise stay in it until the day
    // ends, with nobody to end it. A lesson the room still follows, or one no
    // Agent says anything about, stays on, and is the answer (this PC keeps
    // the folders until it ends). The Agents are asked before the
    // lesson lock is taken; a lesson started or ended meanwhile is gone by as
    // it is then.
    public async Task<ClassLesson?> StandDownForAnotherHomeAsync()
    {
        var token = _stop.Token;
        var ours = Lesson;
        var lessons = ours.IsOn
            ? await LessonsOnTheAgentsAsync(token).ConfigureAwait(false)
            : new List<(KnownMachine Machine, ClassLesson? Lesson)>();
        var newest = ClassLessonOnAgents.Newest(lessons.Select(read => read.Lesson), UnixNow());

        await _lessonGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var lesson = Lesson;
            if (lesson.IsOn)
            {
                if (lesson != ours || !LeftBehind(lesson, newest))
                {
                    return lesson;
                }
                _changingLesson = true;
                await RunningRounds().ConfigureAwait(false);
                _log($"The lesson of {lesson.Turma} ended here: the room's PCs left it behind for another home's.");
                var ended = HeldEndOf(lesson);
                await SendHeldEndAsync(ended, lessons).ConfigureAwait(false);
                // The next lesson here is newer than this end, even in the same second.
                RaiseSavedAtFloor(ended.SavedAt);
                EndUncollected(lesson);
            }
            await ClearLessonLocked().ConfigureAwait(false);
            return null;
        }
        finally
        {
            _changingLesson = false;
            _lessonGate.Release();
        }
    }

    // Whether the newest lesson the room follows says ours is over there. The
    // two SavedAt come from two teacher PCs' clocks (another home's lesson),
    // so clocks far apart could get this wrong; as with generations, a lab's
    // teacher PCs are taken to agree within the time between lessons.
    private static bool LeftBehind(ClassLesson ours, ClassLesson? newest) =>
        newest is not null && newest.SavedAt > ours.SavedAt && (newest.LessonId != ours.LessonId || !newest.IsOn);

    // The newest lesson the online Agents follow (ClassLessonOnAgents.Newest); null for none.
    private async Task<ClassLesson?> NewestOnTheAgentsAsync(CancellationToken token) =>
        ClassLessonOnAgents.Newest((await LessonsOnTheAgentsAsync(token).ConfigureAwait(false)).Select(read => read.Lesson), UnixNow());

    // The lesson each online Agent follows; null for one that gave no answer
    // or is from before the class folder.
    private async Task<List<(KnownMachine Machine, ClassLesson? Lesson)>> LessonsOnTheAgentsAsync(CancellationToken token) =>
        (await Task.WhenAll(AgentsOnline().Select(machine => Task.Run(
            () => (machine, ClassLessonOnAgents.Read(machine, _masterCertificate, _agentPort, _log)), token))).ConfigureAwait(false))
        .ToList();

    // A held lesson's end: one second after it, older than anything the other
    // home did since, which an Agent still in it takes all the same.
    private static ClassLesson HeldEndOf(ClassLesson lesson) =>
        lesson with { SavedAt = lesson.SavedAt == long.MaxValue ? long.MaxValue : lesson.SavedAt + 1, Ended = true };

    // Whether another teacher PC is, or may be, the class folders' home: one
    // this PC saw as the home (ClassHomeFileMaster), or one an online Agent
    // names. A file that can't be read counts as one seen.
    private async Task<bool> AnotherHomeSeenOrNamedAsync()
    {
        long? generation;
        try
        {
            var (onDisk, seen) = await Task.Run(
                () => (ClassHomeFileMaster.Read(_homePath), ClassHomeFileMaster.ReadLastOtherHome(_homePath))).ConfigureAwait(false);
            if (seen is not null)
            {
                return true;
            }
            generation = onDisk ?? _generation;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"What this PC knows of the class folders' home could not be read: {ex.Message}");
            return true;
        }

        var reports = await Task.WhenAll(AgentsOnline().Select(machine => Task.Run(() => HomeOf(machine)))).ConfigureAwait(false);
        return reports.Any(report => report?.Home is { } home && !IsThisPc(home, generation));
    }

    // A held lesson's end (HeldEndOf) to the Agents still in it, with a line
    // for every PC of the room, as an end's: one known to follow another
    // lesson or none isn't sent it (it isn't in this one), though a lesson
    // there this PC knows is over is ended (EndOfLessonOver); one known to
    // have this lesson ended already says so; any other (in it, off, or not
    // answering) is sent it. lessons: what the online Agents follow.
    private async Task<IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)>> SendHeldEndAsync(
        ClassLesson ended, IReadOnlyList<(KnownMachine Machine, ClassLesson? Lesson)> lessons)
    {
        var known = lessons.Where(read => read.Lesson is not null).ToDictionary(read => Key(read.Machine), read => read.Lesson!);
        return await Task.WhenAll(Machines().Select(machine => Task.Run<(KnownMachine Machine, bool Ok, string Detail)>(() =>
        {
            if (!known.TryGetValue(Key(machine), out var followed) || (followed.IsOn && followed.LessonId == ended.LessonId))
            {
                return SendLesson(machine, ended);
            }
            if (followed.LessonId == ended.LessonId)
            {
                return (machine, true, "aula encerrada");
            }
            if (EndOfLessonOver(followed, HomeOf(machine)?.Home) is { } over)
            {
                ClassLessonOnAgents.Send(machine, _masterCertificate, over, _agentPort);
            }
            return (machine, true, ClassFolderText.NotInThisLesson);
        }))).ConfigureAwait(false);
    }

    // The end to hand an Agent that still follows a lesson this PC knows is
    // over: one this PC ran, or a home before it whose lessons map came with
    // the folders, that isn't the lesson here (a PC off since then missed its
    // end, and the next lesson's start). An Agent takes only the end of the
    // lesson it follows, so the end of this PC's own last lesson, or this PC's
    // take-over, would never reach it. The end is one second newer than what
    // the Agent has, no newer than the end it missed, so what this PC hands it
    // next is newer still. A PC that never saw another home (one teacher PC)
    // takes a lesson it never knew, on an Agent whose home (keptHome) carries
    // this Master's own certificate, for one of its own (its lessons map
    // lost), as before two teacher PCs. Null for any other lesson: this PC's
    // own, one only heard of from the Agents (another PC's, which may still be
    // on there), or one it never knew on an Agent that keeps another
    // certificate as the home (another PC, or another Windows account on this
    // one, whose Master may run that lesson now) or none, or once it has seen
    // another home.
    public ClassLesson? EndOfLessonOver(ClassLesson followed, ClassHome? keptHome)
    {
        if (!followed.IsOn || followed.LessonId == Lesson.LessonId
            || (!_store.RanLesson(followed.LessonId) && (_store.KnowsLesson(followed.LessonId) || !OnlyHome(keptHome))))
        {
            return null;
        }
        return followed with { SavedAt = followed.SavedAt == long.MaxValue ? long.MaxValue : followed.SavedAt + 1, Ended = true };
    }

    // Whether keptHome is this Master itself (its certificate: the same PC by
    // name alone may be another Windows account's Master), and this PC never
    // saw another PC as the home nor handed the folders to one
    // (ClassHomeFileMaster); a file that can't be read says nothing of the kind.
    private bool OnlyHome(ClassHome? keptHome)
    {
        if (keptHome is null || !keptHome.IsSameIdentity(_masterCertificate.Thumbprint, _machineName))
        {
            return false;
        }
        try
        {
            return ClassHomeFileMaster.ReadLastOtherHome(_homePath) is null && ClassHomeFileMaster.ReadHandedOverTo(_homePath) is null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"What this PC knows of the class folders' home could not be read: {ex.Message}");
            return false;
        }
    }
}
