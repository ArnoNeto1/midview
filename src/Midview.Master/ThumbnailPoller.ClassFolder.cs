using Midview.Protocol;

namespace Midview.Master;

// ThumbnailPoller: the class folder's chores on the machine's Agent (the
// lesson, and the class folders' home), right after each thumbnail.
public sealed partial class ThumbnailPoller
{
    // The same for the class folder's lesson: a machine that was off when the
    // teacher started or ended it gets it the next time it's on, and the Agent
    // keeps an older one out by SavedAt; one that was busy (it waits for its
    // lesson lock no longer than ClassHomeClaim.MaxLockWait) gets it after
    // BusyRetryInterval, not the usual retry interval. The wait is that
    // lesson's: one started or ended since goes at once. A lesson never saved
    // (SavedAt 0: the Master hasn't had one yet) is nothing to hand over, nor is
    // no lesson at all (a Master that stood down, however saved), which an
    // Agent would take for a newer end of the lesson another teacher PC runs.
    // An Agent that keeps its own newer lesson instead is asked which: one
    // this PC knows is over (the PC was off for its end, and missed this
    // lesson's start) is ended there, and this lesson handed again, as an
    // Agent takes only the end of the lesson it follows. A held lesson's end
    // (classLessonHeldEnd) goes only to an Agent still in that lesson: one
    // with no lesson on would take it, and then hold an end newer than the
    // other home's lesson, which it would refuse.
    private void SendClassLessonIfChanged()
    {
        if (_classLesson?.Invoke() is not { SavedAt: > 0 } lesson || lesson.LessonId == Guid.Empty
            || lesson.SavedAt == _sentClassLessonSavedAt
            || _machine.AgentThumbprint is not { Length: > 0 } agentThumbprint
            || (lesson.SavedAt == _failedClassLessonSavedAt && DateTime.UtcNow < _nextClassLessonAttempt)
            || _stop.IsCancellationRequested)
        {
            return;
        }

        try
        {
            if (!lesson.IsOn && _classLessonHeldEnd?.Invoke() == true && !StillIn(agentThumbprint, lesson))
            {
                _sentClassLessonSavedAt = lesson.SavedAt;
                return;
            }
            var (succeeded, detail) = SendClassLesson(agentThumbprint, lesson);
            if (succeeded && detail.StartsWith(ClassLesson.KeptNewerText, StringComparison.Ordinal) && EndLessonOver(agentThumbprint))
            {
                (succeeded, detail) = SendClassLesson(agentThumbprint, lesson);
            }
            if (succeeded)
            {
                _sentClassLessonSavedAt = lesson.SavedAt;
            }
            else
            {
                // Busy with its student's folders, it is asked again in a moment (an end
                // that waited minutes would leave that PC in the lesson meanwhile).
                ClassLessonFailed(lesson, detail == ClassHomeClaim.BusyText ? BusyRetryInterval : _agentRetryInterval);
            }
        }
        catch (Exception)
        {
            ClassLessonFailed(lesson, _agentRetryInterval);
        }
    }

    private void ClassLessonFailed(ClassLesson lesson, TimeSpan wait)
    {
        _failedClassLessonSavedAt = lesson.SavedAt;
        _nextClassLessonAttempt = DateTime.UtcNow + wait;
    }

    private (bool Succeeded, string Detail) SendClassLesson(string agentThumbprint, ClassLesson lesson) =>
        CommandClient.SendCommandForResult(
            _machine.HostAddress, _agentPort, agentThumbprint, _masterCertificate, MessageType.SetClassLesson, lesson.Encode());

    // Whether the Agent still follows that lesson, on; one that follows a
    // lesson this PC knows is over has it ended there (classLessonOver).
    private bool StillIn(string agentThumbprint, ClassLesson lesson)
    {
        var (followed, _) = ClassFolderClient.GetLesson(_machine, _masterCertificate, _agentPort);
        if (followed is { IsOn: true } && followed.LessonId == lesson.LessonId)
        {
            return true;
        }
        if (followed is not null)
        {
            EndLessonOver(agentThumbprint, followed);
        }
        return false;
    }

    // The lesson the Agent follows (followed, or read now), ended there when
    // this PC knows it is over (classLessonOver), which goes by the home the
    // Agent keeps (keptHome when read already, or read now): true when the
    // Agent took that end.
    private bool EndLessonOver(string agentThumbprint, ClassLesson? followed = null, (ClassHome? Home, bool Read) keptHome = default)
    {
        if (_classLessonOver is null || _stop.IsCancellationRequested)
        {
            return false;
        }
        followed ??= ClassFolderClient.GetLesson(_machine, _masterCertificate, _agentPort).Lesson;
        if (followed is null)
        {
            return false;
        }
        var home = keptHome.Read ? keptHome.Home : ClassFolderClient.GetHome(_machine, _masterCertificate, _agentPort).Home;
        if (_classLessonOver(followed, home) is not { } end)
        {
            return false;
        }
        var (succeeded, detail) = SendClassLesson(agentThumbprint, end);
        return succeeded && !detail.StartsWith(ClassLesson.KeptNewerText, StringComparison.Ordinal);
    }

    // The class folders' home: while this PC keeps them (classHomeClaim), the
    // Agent is claimed with this PC's claim once its machine answers, each
    // time it comes back (once per connection). One that keeps an older home,
    // another PC's or this PC's own at an older generation (a straggler: off
    // when this PC took the folders over), is taken back; a lesson it still
    // follows that this PC knows is over (it came with the old home's
    // folders: the PC was off for its end too) is ended there first, or
    // nobody would ever end it, and the PC would stay out of this PC's
    // lessons until the day ends. One that keeps this PC, or no home, is
    // claimed all the same: the Agent takes it as a refresh of the address,
    // port and certificate it keeps for the home (a new address from the
    // network, Midview reinstalled), so a colleague's PC finds the home where
    // the room's Agents say. One that keeps another PC at least as new is left
    // as it is.
    //
    // Never an Agent that follows a lesson that is on while the home it keeps
    // has another certificate than this Master's: another Windows account on
    // this PC (the same PC to the Agent, by its name) may run that lesson, and
    // the Agent would hand this account the home in the middle of it. It is
    // asked again after the retry interval. A refusal (a lesson of the home
    // it keeps, which the Agent itself refuses a take-over during) is tried
    // again after the retry interval, a straggler's busy one in a moment; a
    // busy refresh after the retry interval, so a busy Agent never holds up
    // every poll for its lock wait (ClassHomeClaim.MaxLockWait).
    private void ClaimClassHome()
    {
        if (_classHomeClaim?.Invoke() is not { } claim || claim.Generation == _checkedClassHomeGeneration
            || _machine.AgentThumbprint is not { Length: > 0 } agentThumbprint
            || DateTime.UtcNow < _nextClassHomeAttempt || _stop.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var (home, oldAgent) = ClassFolderClient.GetHome(_machine, _masterCertificate, _agentPort);
            var behind = home is not null && home.Generation < claim.Generation;
            if (oldAgent || (home is not null && !behind && !home.IsSamePc(_masterCertificate.Thumbprint, claim.Name, claim.Generation)))
            {
                _checkedClassHomeGeneration = claim.Generation;
                return;
            }
            if (behind)
            {
                EndLessonOver(agentThumbprint, keptHome: (home, true));
            }
            if (!IsThisMaster(home) && ClassFolderClient.GetLesson(_machine, _masterCertificate, _agentPort).Lesson is { IsOn: true })
            {
                _nextClassHomeAttempt = DateTime.UtcNow + _agentRetryInterval;
                return;
            }
            var (ok, detail, _) = ClassFolderClient.ClaimHome(_machine, _masterCertificate, claim, _agentPort);
            if (!ok && (behind || detail == ClassHomeClaim.BusyText))
            {
                _nextClassHomeAttempt = DateTime.UtcNow
                    + (behind && detail == ClassHomeClaim.BusyText ? BusyRetryInterval : _agentRetryInterval);
                return;
            }
            _checkedClassHomeGeneration = claim.Generation;
        }
        catch (Exception)
        {
            _nextClassHomeAttempt = DateTime.UtcNow + _agentRetryInterval;
        }
    }

    // Whether the home an Agent keeps carries this Master's own certificate.
    private bool IsThisMaster(ClassHome? home) =>
        home is not null && string.Equals(home.Thumbprint, _masterCertificate.Thumbprint, StringComparison.OrdinalIgnoreCase);
}
