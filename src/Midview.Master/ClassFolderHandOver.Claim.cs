using System.IO;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderHandOver: the room's Agents taking this PC as the class folders'
// home. Every online Agent is claimed with a take-over and a generation newer
// than now and than any home they keep, so no older home can undo it.
//
// An Agent's answer can be lost once the claim went out (the connection reset
// while the Agent still waits for its lesson lock, which it may get later; the
// Agent's PC went off): the Agent may take the claim up to
// ClassHomeClaim.MaxLockWait after it read it, which was before its answer
// was lost. So when no answer said yes and some were lost, this PC waits that
// long, and a margin, from the last of those losses (not from the send: a
// slow dispatch or handshake would cut the wait short), then
// asks the Agents which home they keep: one that keeps this PC at that
// generation took it. One whose answer was lost and that doesn't answer that
// either can't be told: the claim is neither done nor not (Unknown), and
// nothing is put back. A claim that never went out (no connection, no
// handshake) was taken by nobody and needs no wait. An Agent busy with its
// student's folders refuses at once (ClassHomeClaim.BusyText) and never
// applies the claim later: not taken.
public sealed partial class ClassFolderHandOver
{
    // How long after a claim's answer was lost the claim can still be taken:
    // an Agent's lock wait, and a margin for the request on its way.
    internal static readonly TimeSpan DefaultLostAnswerWait = ClassHomeClaim.MaxLockWait + TimeSpan.FromSeconds(2);

    // How a claim reaches a machine's Agent (ClassFolderClient.ClaimHome when
    // null; the Action says the claim is going out), and how the home it keeps
    // is read (ClassFolderClient.GetHome when null): a test's stand-ins for an
    // Agent whose answer gets lost, or that goes off.
    internal Func<KnownMachine, ClassHomeClaim, Action, (bool Ok, string Detail, bool OldAgent)>? SendClaim { get; set; }

    internal Func<KnownMachine, (ClassHome? Home, bool OldAgent)>? ReadHome { get; set; }

    // DefaultLostAnswerWait, or a test's (whose Agents wait less for their lock).
    internal TimeSpan LostAnswerWait { get; set; } = DefaultLostAnswerWait;

    // The clock (milliseconds) and the wait the lost answers are waited out with: a test's, so it doesn't sleep.
    internal Func<long> Milliseconds { get; set; } = () => Environment.TickCount64;

    internal Action<TimeSpan> Sleep { get; set; } = Thread.Sleep;

    private enum Answer
    {
        Took,
        Refused,

        // An Agent from before two teacher PCs: it keeps no home.
        Old,

        // No answer, and the claim never went out: nobody took it.
        Unreached,

        // No answer once the claim went out: it may be taken.
        Lost,
    }

    private enum Outcome
    {
        Taken,
        NotTaken,

        // Some Agent may have taken it, and doesn't say (Unknown names them).
        Unknown,
    }

    // Taken when at least one Agent took it (the others are taken back at
    // this PC's next start); NotTaken with why, as an Agent said it (a lesson
    // on), or that they were busy, old or none answered; Unknown (see the class
    // comment). Generation: the one claimed, newer than now, than every home
    // the Agents report (reported: as just read, else read now) or this PC
    // saw (ClassHomeFileMaster: one that took the room back, say), and than
    // above (a generation another PC gave up, or this PC's own). retryBusy: an
    // Agent busy with its student's folders is asked once more, as a start
    // does (a busy answer is conclusive: it took nothing).
    private ClaimResult Claim(long above = 0, bool retryBusy = false, IReadOnlyList<ClassHome>? reported = null)
    {
        var online = AgentsOnline();
        var homes = reported ?? Homes(online).Select(read => read.Home).OfType<ClassHome>().ToList();
        var seen = SeenHome();
        var generation = Math.Max(
            ClassHomeFileMaster.TakeOverGeneration(
                new DateTimeOffset(_localNow()).UtcTicks, seen is null ? homes : homes.Append(seen)),
            ClassHome.Next(above));
        var claim = new ClassHomeClaim(_machineName, _homePort, TakeOver: true, generation);
        var answers = AskEach(online, machine => ClaimOn(machine, claim, retryBusy));
        foreach (var refused in answers.Where(answer => answer.Answer == Answer.Refused))
        {
            _log($"{refused.Machine.Name} didn't take this PC as the class folders' home: {refused.Detail}");
        }
        // The home this claim takes the room from: the strongest the Agents report, or this PC saw, that isn't this PC.
        var replaced = ClassHomeTracker.Strongest((seen is null ? homes : homes.Append(seen))
            .Where(home => !home.IsSamePc(_certificate.Thumbprint, _machineName, null)));
        if (answers.Any(answer => answer.Answer == Answer.Took))
        {
            return new ClaimResult(Outcome.Taken, string.Empty, generation, Array.Empty<string>(), replaced);
        }

        var lost = answers.Where(answer => answer.Answer == Answer.Lost).ToList();
        if (lost.Count > 0)
        {
            // Until none of them can take it any more; then what they keep is the answer.
            var left = LostAnswerWait - TimeSpan.FromMilliseconds(Milliseconds() - lost.Max(answer => answer.LostAt));
            if (left > TimeSpan.Zero)
            {
                Sleep(left);
            }
            var reread = Homes(online);
            if (reread.Any(read => read.Home is { } home && IsThisPcAt(home, generation)))
            {
                _log("An Agent whose answer was lost took this PC as the class folders' home.");
                return new ClaimResult(Outcome.Taken, string.Empty, generation, Array.Empty<string>(), replaced);
            }
            var silent = lost.Where(answer => !reread.Any(read => read.Machine == answer.Machine && read.Answered))
                .Select(answer => answer.Machine.Name).ToList();
            if (silent.Count > 0)
            {
                _log($"Whether {string.Join(", ", silent)} took this PC as the class folders' home can't be told: they don't answer.");
                return new ClaimResult(Outcome.Unknown, string.Empty, generation, silent);
            }
        }

        var refusals = answers.Where(answer => answer.Answer == Answer.Refused).Select(answer => answer.Detail).ToList();
        var why = refusals.FirstOrDefault(detail => detail.Length > 0 && detail != ClassHomeClaim.BusyText)
            ?? (refusals.Contains(ClassHomeClaim.BusyText) ? ClassFolderText.PcsBusy
                : answers.Any(answer => answer.Answer == Answer.Old) ? ClassFolderText.PcsNeedTheNewMidview
                : ClassFolderText.NoPcTookIt);
        return new ClaimResult(Outcome.NotTaken, why, generation, Array.Empty<string>());
    }

    // After a take-over from a home that doesn't answer: each lesson the
    // room's online Agents follow, on or ended (that home's, which this PC's
    // store never had), is remembered as one heard of, with its class, made
    // here when this PC has none of that name, so what the PCs set aside for it
    // comes into that class at this PC's next lesson instead of staying on the
    // PCs. A lesson already known stays as it is. Never throws: a class that
    // can't be made is logged, and that lesson's files stay on the PCs.
    private void LearnTheRoomsLessons()
    {
        var lessons = AskEach(AgentsOnline(), machine => ClassLessonOnAgents.Read(machine, _certificate, _agentPort, _log));
        foreach (var lesson in lessons.OfType<ClassLesson>().Where(lesson => lesson.LessonId != Guid.Empty).DistinctBy(lesson => lesson.LessonId))
        {
            if (!ClassStore.IsValidName(lesson.Turma) || _store.KnowsLesson(lesson.LessonId))
            {
                continue;
            }
            try
            {
                var turma = lesson.Turma;
                if (!_store.Exists(turma))
                {
                    var (made, why, name) = _store.Create(turma);
                    if (!made)
                    {
                        _log($"The class {turma} of a lesson the room follows could not be made here: {why}");
                        continue;
                    }
                    turma = name;
                }
                _store.RememberLessonHeardOf(lesson.LessonId, turma);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _log($"The lesson of {lesson.Turma} the room follows could not be remembered: {ex.Message}");
            }
        }
    }

    // The home an Agent keeps is this PC, at the generation it just claimed with.
    private bool IsThisPcAt(ClassHome home, long generation) =>
        home.Generation == generation
        && string.Equals(home.Thumbprint, _certificate.Thumbprint, StringComparison.OrdinalIgnoreCase)
        && string.Equals(home.Name, _machineName, StringComparison.OrdinalIgnoreCase);

    // What the machine's Agent answered the claim; with no answer, when it was
    // lost if the claim had gone out by then. retryBusy: busy, it is asked once more.
    private ClaimAnswer ClaimOn(KnownMachine machine, ClassHomeClaim claim, bool retryBusy)
    {
        var wentOut = 0;
        void Sending() => Volatile.Write(ref wentOut, 1);
        (bool Ok, string Detail, bool OldAgent) Send() => SendClaim is { } send
            ? send(machine, claim, Sending)
            : ClassFolderClient.ClaimHome(machine, _certificate, claim, _agentPort, Sending);
        try
        {
            var (ok, detail, oldAgent) = Send();
            if (retryBusy && !ok && !oldAgent && detail == ClassHomeClaim.BusyText)
            {
                _log($"{machine.Name} was busy ({detail}); asking it again.");
                (ok, detail, oldAgent) = Send();
            }
            return new ClaimAnswer(machine, oldAgent ? Answer.Old : ok ? Answer.Took : Answer.Refused, detail, 0);
        }
        catch (Exception ex)
        {
            ClassLessonOnAgents.LogUnexpected(_log, ex, $"Claiming the class folders' home on {machine.Name} failed");
            if (Volatile.Read(ref wentOut) == 0)
            {
                return new ClaimAnswer(machine, Answer.Unreached, string.Empty, 0);
            }
            _log($"{machine.Name}'s answer to the claim was lost after the claim went out: {ex.Message}");
            return new ClaimAnswer(machine, Answer.Lost, string.Empty, Milliseconds());
        }
    }

    // The home each machine's Agent keeps: Answered false for one that didn't
    // answer; a null Home for one that keeps none or is from before two teacher PCs.
    private List<HomeRead> Homes(IReadOnlyList<KnownMachine> machines) =>
        AskEach(machines, machine =>
        {
            try
            {
                var (home, _) = ReadHome is { } read ? read(machine) : ClassFolderClient.GetHome(machine, _certificate, _agentPort);
                return new HomeRead(machine, true, home);
            }
            catch (Exception ex)
            {
                ClassLessonOnAgents.LogUnexpected(_log, ex, $"Reading the class folders' home of {machine.Name} failed");
                return new HomeRead(machine, false, null);
            }
        }).ToList();

    // The last other home this PC saw (ClassHomeFileMaster); null for none, or when it can't be read.
    private ClassHome? SeenHome()
    {
        try
        {
            return ClassHomeFileMaster.ReadLastOtherHome(_homePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"The class folders' home this PC saw could not be read: {ex.Message}");
            return null;
        }
    }

    // The home taken over from while it didn't answer, as of now (ClassHomeFileMaster.ReadTakenOverFrom); null: none.
    private void KeepTakenOverFrom(ClassHome? home)
    {
        try
        {
            ClassHomeFileMaster.WriteTakenOverFrom(home, new DateTimeOffset(_localNow()).ToUnixTimeSeconds(), _homePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log($"The class folders' home taken over from could not be saved: {ex.Message}");
        }
    }

    private void KeepGeneration(long generation)
    {
        try
        {
            ClassHomeFileMaster.Write(generation, _homePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The Agents keep it: this PC's next start finds itself among them as the home.
            _log($"This PC's generation as the class folders' home could not be saved: {ex.Message}");
        }
    }

    // The online machines whose Agent the classroom list knows.
    private List<KnownMachine> AgentsOnline() => ClassLessonOnAgents.Online(_machines, _isOnline, _log);

    // Each machine at once, each on a thread of its own: a claim can wait on
    // an Agent's lock for MaxLockWait, and many of those mustn't queue behind
    // each other on the pool (their claims would go out late).
    private static T[] AskEach<T>(IReadOnlyList<KnownMachine> machines, Func<KnownMachine, T> ask) =>
        Task.WhenAll(machines.Select(machine => Task.Factory.StartNew(
            () => ask(machine), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)))
            .GetAwaiter().GetResult();

    private sealed record ClaimAnswer(KnownMachine Machine, Answer Answer, string Detail, long LostAt);

    private sealed record HomeRead(KnownMachine Machine, bool Answered, ClassHome? Home);

    // Replaced: the home the room is taken from (Claim); null for none known.
    private sealed record ClaimResult(
        Outcome Outcome, string Detail, long Generation, IReadOnlyList<string> Unknown, ClassHome? Replaced = null);
}
