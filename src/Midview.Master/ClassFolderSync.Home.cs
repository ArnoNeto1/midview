using System.IO;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderSync: the class folders' home, with two teacher PCs in one lab
// (plan decisions 2, 8 and 9, Rulings 9, 10, 18 and 19). A start first claims
// the home on every online Agent, going by generations (ClassHome.Generation);
// a PC that stops being the home stands down; and a lesson another PC ended
// straight on the Agents, while this PC's Master was closed, is adopted. An
// Agent busy with its student's folders refuses a claim as busy
// (ClassHomeClaim.BusyText): it took nothing, as one that didn't answer. It is
// asked once more; still busy, and its lesson refused too (it still keeps an
// older home), its line says it is busy rather than naming that home.
public sealed partial class ClassFolderSync
{
    // How often, while a lesson is on, the Agents are asked whether another PC ended it.
    private static readonly TimeSpan AdoptInterval = TimeSpan.FromSeconds(30);

    // The generation this run claimed with, for when its file (ClassHomeFileMaster)
    // couldn't be saved or read back. Under the lesson lock.
    private long? _generation;

    // How many take-overs of the class folders run on this PC (BeginTakeOver):
    // no lesson starts while any does. Raised under the lesson lock, so a
    // start in progress finishes first.
    private int _takingOver;

    // For a PC that stopped being the home (another one took the class folders
    // over): rounds in progress are waited for, as for a lesson change, then the
    // saved lesson is cleared to no lesson ever saved (ClassLesson.None(0)), which
    // the pollers never hand an Agent: a newer "no lesson" would end the new
    // home's lesson on every PC. LessonChanged says so; a Master opened again
    // doesn't carry on with the lesson, and the loop has nothing left to do. The
    // store is left as it is, and a later StartAsync, with the folders back on
    // this PC, starts lessons again. Waits for a start or an end in progress, so
    // never from a handler of this sync's own events (raised while it may hold
    // its lesson lock); nothing when there is no lesson to clear.
    public async Task StandDownAsync()
    {
        await _lessonGate.WaitAsync(_stop.Token).ConfigureAwait(false);
        try
        {
            await ClearLessonLocked().ConfigureAwait(false);
        }
        finally
        {
            _changingLesson = false;
            _lessonGate.Release();
        }
    }

    // StandDownAsync, unless a lesson is on: then nothing changes, and that
    // lesson is the answer (null once stood down, or with no lesson to clear).
    // The look and the stand-down are one step under the lesson lock (Ruling
    // 22): a lesson starting meanwhile is either on when it looks, or starts
    // after (and the Agents that keep the new home refuse it), never cleared.
    public async Task<ClassLesson?> StandDownIfNoLessonAsync()
    {
        await _lessonGate.WaitAsync(_stop.Token).ConfigureAwait(false);
        try
        {
            if (Lesson is { IsOn: true } on)
            {
                return on;
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

    // A take-over of the class folders begins on this PC (ClassFolderHandOver):
    // no lesson starts until EndTakeOver (a start says why, on every PC's
    // line), so what the take-over puts back or throws away never holds files a
    // lesson here brought in (Ruling 22). False, and nothing held, while a
    // lesson is on here (it may be bringing files in): the take-over doesn't
    // begin. Waits for a start or an end in progress; on the caller's thread.
    internal bool BeginTakeOver()
    {
        _lessonGate.Wait(_stop.Token);
        try
        {
            if (Lesson.IsOn)
            {
                return false;
            }
            Interlocked.Increment(ref _takingOver);
            return true;
        }
        finally
        {
            _lessonGate.Release();
        }
    }

    internal void EndTakeOver() => Interlocked.Decrement(ref _takingOver);

    // How many copies of this PC's class folders go to a colleague's PC, or
    // went and haven't settled (ClassFolderHandOver): no lesson starts while
    // any does. Raised under the lesson lock, as _takingOver.
    private int _giving;

    // A copy of the class folders begins to go to a colleague's PC
    // (ClassFolderHandOver.Send): no lesson starts until EndGiving (a start
    // says why, on every PC's line), so no start here meets that PC's claims
    // on the Agents, which would split the room. False, and nothing held,
    // while a lesson is on. Waits for a start or an end in progress; on the
    // caller's thread.
    internal bool BeginGiving()
    {
        _lessonGate.Wait(_stop.Token);
        try
        {
            if (Lesson.IsOn)
            {
                return false;
            }
            Interlocked.Increment(ref _giving);
            return true;
        }
        finally
        {
            _lessonGate.Release();
        }
    }

    internal void EndGiving() => Interlocked.Decrement(ref _giving);

    // With the lesson lock held: the lesson cleared to ClassLesson.None(0)
    // (StandDownAsync), after the rounds in progress; nothing when it is already.
    private async Task ClearLessonLocked()
    {
        var lesson = Lesson;
        if (lesson == ClassLesson.None(0))
        {
            return;
        }

        _changingLesson = true;
        await RunningRounds().ConfigureAwait(false);
        RaiseSavedAtFloor(lesson.SavedAt);
        Adopt(ClassLesson.None(0));
    }

    // A lesson another teacher PC ended straight on the Agents (this PC's
    // Master was closed, say): when the newest lesson the online Agents follow
    // (not saved more than a day ahead of this PC's clock) is the one on here,
    // ended and newer, it ends here as a lesson nobody collected (no last
    // rounds, the files kept, every version known of them remembered), so what
    // the PCs set aside comes in at the next lesson. What is adopted is this
    // lesson, ended when the Agents say. Nothing otherwise. Run at construction
    // and every 30 s while a lesson is on. Throws nothing but
    // OperationCanceledException, after Dispose.
    public async Task AdoptAgentsEndAsync()
    {
        var token = _stop.Token;
        var ours = Lesson;
        if (!ours.IsOn)
        {
            return;
        }

        var newest = await NewestOnTheAgentsAsync(token).ConfigureAwait(false);
        if (newest is null || newest.LessonId != ours.LessonId || !newest.Ended || newest.SavedAt <= ours.SavedAt)
        {
            return;
        }

        await _lessonGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (Lesson != ours)
            {
                return; // started, ended or adopted meanwhile
            }

            _changingLesson = true;
            await RunningRounds().ConfigureAwait(false);
            EndUncollected(ours);
            Adopt(ours with { SavedAt = newest.SavedAt, Ended = true });
        }
        finally
        {
            _changingLesson = false;
            _lessonGate.Release();
        }
    }

    // The claim (plan decision 2, by generations, Ruling 9). First every online
    // Agent says which home it keeps; an Agent from before two teacher PCs keeps
    // none and takes any start, and one that doesn't answer says nothing. When
    // one keeps another PC as home with a newer generation than this PC's (or
    // this PC was never the home, and has none), that PC is the home: nothing is
    // claimed, saved or sent, and the answer is those Agents' refusals, each
    // naming it. Otherwise this PC claims the home on each of them, taking back
    // (TakeOver) those that keep an older home: they were off when this PC took
    // the folders over. Null then. A PC with no generation on file goes by the
    // one the Agents recorded for it, and one that was never the home claims
    // with now's; either is kept once an Agent took the claim. But a PC that
    // was never the home and saw another one as the home (ClassHomeFileMaster,
    // Ruling 11) is refused as if every PC still kept it: it becomes the home
    // only by taking the folders over; so is one whose generation is older
    // than another home it saw (Ruling 22). And a file of this PC's that is there
    // but can't be read refuses the start too, saying so (Ruling 12): it isn't
    // taken for "never the home". A refusal of either kind has a line for
    // every PC of the room. Busy: the PCs still busy after being asked twice.
    // Claims refused for another home (another PC claimed the room at the
    // same moment) are settled before anything starts (SettleWithAnotherClaimAsync).
    private async Task<HomeClaim> ClaimHomeAsync()
    {
        long? onDisk;
        ClassHome? seen;
        try
        {
            (onDisk, seen) = await Task.Run(
                () => (ClassHomeFileMaster.Read(_homePath), ClassHomeFileMaster.ReadLastOtherHome(_homePath))).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ruling 12: not "never the home", which would claim with now.
            _log($"What this PC knows of the class folders' home could not be read: {ex.Message}");
            return HomeClaim.Refused(EveryPc(ClassFolderText.HomeFileUnreadable(ErrorText.Describe(ex))));
        }

        var saved = onDisk ?? _generation;
        var reports = (await Task.WhenAll(AgentsOnline().Select(machine => Task.Run(() => HomeOf(machine))))
            .ConfigureAwait(false)).OfType<HomeReport>().ToList();

        var thisPc = reports.Where(report => report.Home is { } home && IsThisPc(home, saved)).ToList();
        var others = reports.Where(report => report.Home is { } home && !IsThisPc(home, saved)).ToList();

        // The newest of the one on file and the ones the Agents recorded for
        // this PC (Ruling 21: a take-over this PC made that its file doesn't
        // have, an older generation kept from when it was the home before);
        // never one an Agent keeps for another PC. Saved once an Agent takes the claim.
        var recorded = thisPc.Select(report => (long?)report.Home!.Generation).Where(generation => generation > 0).Max();
        var ours = saved is { } kept && recorded is { } fromAgents ? Math.Max(kept, fromAgents) : saved ?? recorded;
        var newer = others.Where(report => ours is not { } generation || report.Home!.Generation > generation).ToList();
        if (newer.Count > 0)
        {
            RememberOtherHome(newer.MaxBy(report => report.Home!.Generation)!.Home!);
            return HomeClaim.Refused(newer.Select(report => (report.Machine, false, report.Home!.RefusalText)).ToList());
        }

        // Ruling 11: never the home, but it saw another PC as the home: as if
        // every PC still said so (Ruling 12: every PC's line, even with no
        // Agent answering). Ruling 22: the same for another home seen newer
        // than this PC's own (one that took the room back after this PC gave
        // a take-over up, or that this PC handed the folders to), even with
        // only Agents naming this PC on (stragglers, older).
        if (seen is not null && (ours is null || (seen.Generation > ours && !IsThisPc(seen, ours))))
        {
            return HomeClaim.Refused(EveryPc(seen.RefusalText));
        }

        var claimed = ours ?? new DateTimeOffset(_localNow()).UtcTicks;
        var answers = (await Task.WhenAll(reports.Select(report =>
                Task.Run(() => Claim(report.Machine, takeOver: others.Contains(report), claimed))))
            .ConfigureAwait(false)).OfType<ClaimAnswer>().ToList();
        foreach (var answer in answers.Where(answer => !answer.Ok))
        {
            // Its lesson start says so too.
            _log($"{answer.Machine.Name} didn't take this PC as the class folders' home: {answer.Detail}");
        }
        var busy = answers.Where(answer => answer.Busy).Select(answer => Key(answer.Machine)).ToHashSet();
        if (answers.Any(answer => answer.NamesAnotherHome))
        {
            return await SettleWithAnotherClaimAsync(reports, claimed, onDisk, busy).ConfigureAwait(false);
        }
        // Against what is on disk, so a generation that couldn't be saved is saved next time.
        if (onDisk != claimed && answers.Any(answer => answer.Ok))
        {
            KeepGeneration(claimed);
        }
        return new HomeClaim(null, busy, claimed);
    }

    // A start's lines: a PC still busy at the claim whose lesson was refused
    // too (it still keeps the home it would have given up) says it was busy.
    private static IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)> SayBusy(
        IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)> lines, IReadOnlySet<string> busy) =>
        busy.Count == 0
            ? lines
            : lines.Select(line => !line.Ok && busy.Contains(Key(line.Machine)) ? (line.Machine, false, ClassHomeClaim.BusyText) : line)
                .ToList();

    // What a cancelled start answers: one line for every PC of the room, as a
    // start's lesson goes to every one (SendToEveryAgentAsync).
    private List<(KnownMachine Machine, bool Ok, string Detail)> EveryPc(string detail) =>
        Machines().Select(machine => (machine, false, detail)).ToList();

    private bool IsThisPc(ClassHome home, long? generation) =>
        home.IsSamePc(_masterCertificate.Thumbprint, _machineName, generation);

    // A start refused for another home has seen it (Ruling 11), as the tracker does.
    private void RememberOtherHome(ClassHome home)
    {
        try
        {
            ClassHomeFileMaster.WriteLastOtherHome(home, _homePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"The class folders' home seen could not be saved: {ex.Message}");
        }
    }

    private void KeepGeneration(long generation)
    {
        _generation = generation;
        try
        {
            ClassHomeFileMaster.Write(generation, _homePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"This PC's generation as the class folders' home could not be saved: {ex.Message}");
        }
    }

    // The home the machine's Agent keeps (none for one from before two teacher
    // PCs), or null when it gave no answer.
    private HomeReport? HomeOf(KnownMachine machine)
    {
        try
        {
            var (home, oldAgent) = ClassFolderClient.GetHome(machine, _masterCertificate, _agentPort);
            return oldAgent ? null : new HomeReport(machine, home);
        }
        catch (Exception ex)
        {
            LogUnexpected(ex, $"Reading the class folders' home of {machine.Name} failed");
            return null;
        }
    }

    // What the machine's Agent answered the claim, or null when it gave no
    // answer. Busy, it is asked once more (a busy Agent took nothing).
    private ClaimAnswer? Claim(KnownMachine machine, bool takeOver, long generation)
    {
        try
        {
            var claim = new ClassHomeClaim(_machineName, _homePort, takeOver, generation);
            var (ok, detail, oldAgent) = ClassFolderClient.ClaimHome(machine, _masterCertificate, claim, _agentPort);
            if (!ok && detail == ClassHomeClaim.BusyText)
            {
                _log($"{machine.Name} was busy ({detail}); asking it again.");
                (ok, detail, oldAgent) = ClassFolderClient.ClaimHome(machine, _masterCertificate, claim, _agentPort);
            }
            return new ClaimAnswer(machine, ok || oldAgent, detail);
        }
        catch (Exception ex)
        {
            LogUnexpected(ex, $"Claiming the class folders' home on {machine.Name} failed");
            return null;
        }
    }

    // The online machines whose Agent the classroom list knows.
    private List<KnownMachine> AgentsOnline() =>
        Machines().Where(machine => machine.AgentThumbprint is { Length: > 0 } && IsOnline(machine)).ToList();

    // A machine off, unreachable or not trusting this teacher is no news.
    private void LogUnexpected(Exception ex, string what) => ClassLessonOnAgents.LogUnexpected(_log, ex, what);

    // At construction, then every AdoptInterval (AdoptAgentsEndAsync does
    // nothing with no lesson on).
    private async Task AdoptLoopAsync()
    {
        var token = _stop.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await AdoptAgentsEndAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log($"Adopting an end made on the Agents failed: {ex}");
            }

            try
            {
                await Task.Delay(AdoptInterval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private sealed record HomeReport(KnownMachine Machine, ClassHome? Home);

    private sealed record ClaimAnswer(KnownMachine Machine, bool Ok, string Detail)
    {
        public bool Busy => !Ok && Detail == ClassHomeClaim.BusyText;

        // Refused for another PC that the Agent keeps as the home.
        public bool NamesAnotherHome => !Ok && Detail.StartsWith(ClassHomeClaim.OtherHomeText, StringComparison.Ordinal);
    }

    // What a start's claim came to: the refusals when another PC is the home
    // (nothing starts), the PCs (by Key) still busy after two tries, and the
    // generation this PC claimed the room with.
    private sealed record HomeClaim(
        IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)>? Refusals, IReadOnlySet<string> Busy, long Generation = 0)
    {
        public static HomeClaim Refused(IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)> refusals) =>
            new(refusals, new HashSet<string>());
    }
}
