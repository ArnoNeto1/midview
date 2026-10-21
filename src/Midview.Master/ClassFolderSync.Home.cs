using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderSync: the class folders' home, with two teacher PCs in one lab
// (plan decisions 2, 8 and 9). A start first claims the home on every online
// Agent; a PC that stops being the home stands down; and a lesson another PC
// ended straight on the Agents, while this PC's Master was closed, is adopted.
public sealed partial class ClassFolderSync
{
    // How often, while a lesson is on, the Agents are asked whether another PC ended it.
    private static readonly TimeSpan AdoptInterval = TimeSpan.FromSeconds(30);

    // For a PC that stopped being the home (another one took the class folders
    // over): the saved lesson is cleared, so a Master opened again doesn't carry
    // on with it, and LessonChanged says so. With no lesson the loop has nothing
    // left to do (a round in the middle stops at its next file). The store is
    // left as it is, and a later StartAsync, with the folders back on this PC,
    // starts lessons again. Waits for a start or an end in progress, so never
    // from a handler of this sync's own events (raised while it may hold its
    // lesson lock); nothing when there is no lesson to clear.
    public void StandDown()
    {
        _lessonGate.Wait();
        try
        {
            if (Lesson.LessonId != Guid.Empty)
            {
                Adopt(ClassLesson.None(NextSavedAt()));
            }
        }
        finally
        {
            _lessonGate.Release();
        }
    }

    // A lesson another teacher PC ended straight on the Agents (this PC's
    // Master was closed, say): when the newest lesson the online Agents follow
    // is the one on here, ended and newer, it ends here as a lesson nobody
    // collected (no last rounds, the files kept, every version known of them
    // remembered), so what the PCs set aside comes in at the next lesson.
    // Nothing otherwise. Run at construction and every 30 s while a lesson is
    // on. Throws nothing but OperationCanceledException, after Dispose.
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
        var newest = lessons.OfType<ClassLesson>().MaxBy(lesson => lesson.SavedAt);
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
            Adopt(newest);
        }
        finally
        {
            _changingLesson = false;
            _lessonGate.Release();
        }
    }

    // The claim (plan decision 2): "this PC is the class folders' home", to
    // every online Agent at once. An Agent from before two teacher PCs takes
    // any start, so it counts as taking the claim; one that can't be reached
    // doesn't count. Null when this PC is the home: no more than half of those
    // that answered refused naming another PC. Then those few, which another
    // teacher PC claimed while this one wasn't looking (they were off at this
    // PC's lessons, say), are taken back (TakeOver). Otherwise the refusals.
    private async Task<IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)>?> ClaimHomeAsync()
    {
        var answers = await Task.WhenAll(AgentsOnline().Select(machine => Task.Run(() => Claim(machine, takeOver: false))))
            .ConfigureAwait(false);
        var answered = answers.OfType<ClaimAnswer>().ToList();
        var otherHome = answered
            .Where(answer => !answer.Ok && answer.Detail.StartsWith(ClassHomeClaim.OtherHomeText, StringComparison.Ordinal))
            .ToList();
        if (otherHome.Count * 2 > answered.Count)
        {
            return otherHome.Select(answer => (answer.Machine, false, answer.Detail)).ToList();
        }

        var takenBack = await Task.WhenAll(otherHome.Select(answer => Task.Run(() => Claim(answer.Machine, takeOver: true))))
            .ConfigureAwait(false);
        foreach (var answer in takenBack.OfType<ClaimAnswer>().Where(answer => !answer.Ok))
        {
            // Its lesson start says so too.
            _log($"{answer.Machine.Name} kept another PC as the class folders' home: {answer.Detail}");
        }
        return null;
    }

    // What the machine's Agent answered the claim (Ok for an Agent from before
    // two teacher PCs), or null when it gave no answer.
    private ClaimAnswer? Claim(KnownMachine machine, bool takeOver)
    {
        try
        {
            var (ok, detail, oldAgent) = ClassFolderClient.ClaimHome(
                machine, _masterCertificate, new ClassHomeClaim(_machineName, _homePort, takeOver), _agentPort);
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

    private sealed record ClaimAnswer(KnownMachine Machine, bool Ok, string Detail);
}
