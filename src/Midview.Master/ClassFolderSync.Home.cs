using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderSync: the class folders' home, with two teacher PCs in one lab
// (plan decisions 2, 8 and 9, Rulings 9 and 10). A start first claims the home
// on every online Agent, going by generations (ClassHome.Generation); a PC that
// stops being the home stands down; and a lesson another PC ended straight on
// the Agents, while this PC's Master was closed, is adopted.
public sealed partial class ClassFolderSync
{
    // How often, while a lesson is on, the Agents are asked whether another PC ended it.
    private static readonly TimeSpan AdoptInterval = TimeSpan.FromSeconds(30);

    // An end saved further ahead of this PC's clock than this is not believed.
    private static readonly TimeSpan AdoptHorizon = TimeSpan.FromDays(1);

    // The generation this run claimed with, for when its file (ClassHomeFileMaster)
    // couldn't be saved or read back. Under the lesson lock.
    private long? _generation;

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
        finally
        {
            _changingLesson = false;
            _lessonGate.Release();
        }
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

        var lessons = await Task.WhenAll(AgentsOnline().Select(machine => Task.Run(() => LessonOf(machine), token)))
            .ConfigureAwait(false);
        var horizon = UnixNow() + (long)AdoptHorizon.TotalSeconds;
        var newest = lessons.OfType<ClassLesson>().Where(lesson => lesson.SavedAt <= horizon).MaxBy(lesson => lesson.SavedAt);
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
    // Ruling 11) is refused as if every Agent that answered still kept it: it
    // becomes the home only by taking the folders over.
    private async Task<IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)>?> ClaimHomeAsync()
    {
        var saved = ClassHomeFileMaster.Read(_homePath) ?? _generation;
        var reports = (await Task.WhenAll(AgentsOnline().Select(machine => Task.Run(() => HomeOf(machine))))
            .ConfigureAwait(false)).OfType<HomeReport>().ToList();

        var thisPc = reports.Where(report => report.Home is { } home && IsThisPc(home, saved)).ToList();
        var others = reports.Where(report => report.Home is { } home && !IsThisPc(home, saved)).ToList();

        // With none on file, the one the Agents recorded for this PC, if any.
        var ours = saved ?? thisPc.Select(report => (long?)report.Home!.Generation).Where(generation => generation > 0).Max();
        var newer = others.Where(report => ours is not { } generation || report.Home!.Generation > generation).ToList();
        if (newer.Count > 0)
        {
            RememberOtherHome(newer.MaxBy(report => report.Home!.Generation)!.Home!);
            return newer.Select(report => (report.Machine, false, report.Home!.RefusalText)).ToList();
        }

        // Ruling 11: never the home, but it saw another PC as the home: as if
        // every Agent that answered still said so.
        if (ours is null && ClassHomeFileMaster.ReadLastOtherHome(_homePath) is { } seen)
        {
            return reports.Select(report => (report.Machine, false, seen.RefusalText)).ToList();
        }

        var claimed = ours ?? new DateTimeOffset(_localNow()).UtcTicks;
        var answers = await Task.WhenAll(reports.Select(report =>
                Task.Run(() => Claim(report.Machine, takeOver: others.Contains(report), claimed))))
            .ConfigureAwait(false);
        foreach (var answer in answers.OfType<ClaimAnswer>().Where(answer => !answer.Ok))
        {
            // Its lesson start says so too.
            _log($"{answer.Machine.Name} didn't take this PC as the class folders' home: {answer.Detail}");
        }
        if (saved != claimed && answers.OfType<ClaimAnswer>().Any(answer => answer.Ok))
        {
            KeepGeneration(claimed);
        }
        return null;
    }

    private bool IsThisPc(ClassHome home, long? generation) =>
        home.IsSamePc(_masterCertificate.Thumbprint, _machineName, generation);

    // A start refused for another home has seen it (Ruling 11), as the tracker does.
    private void RememberOtherHome(ClassHome home)
    {
        try
        {
            ClassHomeFileMaster.WriteLastOtherHome(SeenHome.Of(home), _homePath);
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

    // What the machine's Agent answered the claim, or null when it gave no answer.
    private ClaimAnswer? Claim(KnownMachine machine, bool takeOver, long generation)
    {
        try
        {
            var (ok, detail, oldAgent) = ClassFolderClient.ClaimHome(
                machine, _masterCertificate, new ClassHomeClaim(_machineName, _homePort, takeOver, generation), _agentPort);
            return new ClaimAnswer(machine, ok || oldAgent, detail);
        }
        catch (Exception ex)
        {
            LogUnexpected(ex, $"Claiming the class folders' home on {machine.Name} failed");
            return null;
        }
    }

    // The lesson the machine's Agent follows; null when it gave no answer or
    // is from before the class folder.
    private ClassLesson? LessonOf(KnownMachine machine)
    {
        try
        {
            return ClassFolderClient.GetLesson(machine, _masterCertificate, _agentPort).Lesson;
        }
        catch (Exception ex)
        {
            LogUnexpected(ex, $"Reading the lesson of {machine.Name} failed");
            return null;
        }
    }

    // The online machines whose Agent the classroom list knows.
    private List<KnownMachine> AgentsOnline() =>
        Machines().Where(machine => machine.AgentThumbprint is { Length: > 0 } && IsOnline(machine)).ToList();

    // A machine off, unreachable or not trusting this teacher is no news.
    private void LogUnexpected(Exception ex, string what)
    {
        if (ex is not (SocketException or IOException or AuthenticationException or OperationCanceledException))
        {
            _log($"{what}: {ex}");
        }
    }

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

    private sealed record ClaimAnswer(KnownMachine Machine, bool Ok, string Detail);
}
