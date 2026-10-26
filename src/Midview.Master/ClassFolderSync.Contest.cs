using Midview.Protocol;

namespace Midview.Master;

// ClassFolderSync: two teacher PCs claiming the room at the same moment (two
// first lessons started at once, say). Each Agent takes whichever claim
// reaches it first, so the room may be split between the two, or all of it
// the other's; a start that went on regardless would split the room between
// two lessons, or leave a lesson on here that no PC follows.
//
// Which of the two keeps the room is decided the same way on both PCs: the
// newer generation (ClassHome.Generation), and between equal ones the
// computer name that sorts last, ignoring case. A PC that doesn't keep it
// starts nothing, and nor does one that holds no PC of the room at all. The
// one that keeps it takes the other's PCs back, with a generation newer than
// the other's, before its lesson goes out. And a lesson that went out anyway
// (the two PCs didn't see the same PCs online, say) is taken back when a PC
// refuses it for a home that outranks this one.
public sealed partial class ClassFolderSync
{
    // The PC whose lesson was on where this PC couldn't take its PCs back
    // (SettleWithAnotherClaimAsync), and that lesson (the ones those PCs
    // followed), until it is over. Under the lesson lock.
    private (ClassHome Home, IReadOnlySet<Guid> Lessons)? _contestRival;

    // A start's claims were refused for another home (ClaimHomeAsync): the
    // Agents are asked again which home they keep. When that PC outranks
    // this one (Outranks), or no Agent keeps this PC, nothing starts: every
    // PC's line names that PC, which this PC has now seen (as a refusal
    // remembers it). Otherwise this PC takes that PC's Agents back with a
    // generation newer than it (and brings its own up to it), and keeps that
    // generation; one that refuses because a lesson of that PC is on there
    // (it started first) means no lesson starts here either: a second lesson
    // would split the room. The folders come to this PC once that lesson ends
    // (its generation is the newer). claimed: the generation this PC claimed
    // with; onDisk: the one on file; busy: the PCs busy at the claims.
    private async Task<HomeClaim> SettleWithAnotherClaimAsync(
        IReadOnlyList<HomeReport> reports, long claimed, long? onDisk, IReadOnlySet<string> busy)
    {
        var reread = (await Task.WhenAll(reports.Select(report => Task.Run(() => HomeOf(report.Machine)))).ConfigureAwait(false))
            .OfType<HomeReport>().ToList();
        var rival = Strongest(reread.Select(read => read.Home).OfType<ClassHome>().Where(home => !IsThisPc(home, claimed)));
        var holds = reread.Any(read => read.Home is { } home && IsThisPc(home, claimed));
        if (rival is null)
        {
            if (holds && onDisk != claimed)
            {
                KeepGeneration(claimed);
            }
            return new HomeClaim(null, busy, claimed);
        }
        // A rival at the latest generation can't be taken back (nothing is newer): it keeps the room.
        if (!holds || Outranks(rival, claimed, _machineName) || rival.Generation >= ClassHome.MaxGeneration)
        {
            _log($"{rival.Name} claimed the class folders at the same time and keeps them: nothing starts here.");
            RememberOtherHome(rival);
            return HomeClaim.Refused(EveryPc(rival.RefusalText));
        }

        var generation = rival.Generation < claimed ? claimed : ClassHome.Next(rival.Generation);
        _log($"{rival.Name} claimed the class folders at the same time; this PC keeps them (generation {generation}).");
        var answers = (await Task.WhenAll(reread
                .Where(read => read.Home is { } home && (!IsThisPc(home, claimed) || generation != claimed))
                .Select(read => Task.Run(() => Claim(read.Machine, takeOver: !IsThisPc(read.Home!, claimed), generation))))
            .ConfigureAwait(false)).OfType<ClaimAnswer>().ToList();
        if (onDisk != generation)
        {
            KeepGeneration(generation);
        }
        var inItsLesson = answers.Where(answer => !answer.Ok && answer.Detail == ClassFolderText.EndLessonFirst)
            .Select(answer => answer.Machine).ToList();
        if (inItsLesson.Count > 0)
        {
            _log($"{rival.Name}'s lesson is on before this PC took its PCs back: nothing starts here until it ends.");
            var lessons = await Task.WhenAll(inItsLesson.Select(machine => Task.Run(
                () => ClassLessonOnAgents.Read(machine, _masterCertificate, _agentPort, _log)))).ConfigureAwait(false);
            var on = lessons.Where(lesson => lesson is { IsOn: true }).Select(lesson => lesson!.LessonId).ToHashSet();
            _contestRival = on.Count > 0 ? (rival, on) : null;
            return HomeClaim.Refused(EveryPc(ClassFolderText.OtherPcsLessonFirst(NameOf(rival))));
        }
        return new HomeClaim(
            null, busy.Union(answers.Where(answer => answer.Busy).Select(answer => Key(answer.Machine))).ToHashSet(), generation);
    }

    // A start after one that met the rival's lesson (_contestRival): while a
    // PC refuses a take-over for that lesson, still on there, and no online
    // Agent says it ended, no lesson starts here (that PC never heard of this
    // one, so the rival's lesson goes on): the rival, and every PC's line says
    // to try once it ends. Null otherwise, and the rival forgotten: once that
    // lesson ended, a PC still in it (off for its end) is a straggler of an
    // older home, which leaves only that PC out.
    private async Task<ClassHome?> RivalsLessonStillOnAsync(IReadOnlyList<ClaimAnswer> answers)
    {
        if (_contestRival is not { } rival)
        {
            return null;
        }
        var followed = (await LessonsOnTheAgentsAsync(_stop.Token).ConfigureAwait(false))
            .Where(read => read.Lesson is { } lesson && rival.Lessons.Contains(lesson.LessonId)).ToList();
        if (!followed.Any(read => !read.Lesson!.IsOn)
            && answers.Any(answer => !answer.Ok && answer.Detail == ClassFolderText.EndLessonFirst
                && followed.Any(read => read.Machine == answer.Machine)))
        {
            _log($"{rival.Home.Name}'s lesson is still on where this PC couldn't take its PCs back: nothing starts here.");
            return rival.Home;
        }
        _contestRival = null;
        return null;
    }

    private static string NameOf(ClassHome home) => home.Name.Length > 0 ? home.Name : ClassHome.UnnamedText;

    // After a start's lesson went to the Agents: one that refused it for a
    // home that outranks this PC (it claimed the room meanwhile, on PCs this
    // PC didn't claim: off for this PC, say) means the room is that PC's.
    // The lesson is ended as a held lesson's end (SendHeldEndAsync: one second
    // after it, never newer than that PC's lesson of the same moment): on the
    // Agents still in it, and on those that can't be read (one whose answer
    // was lost may have taken it), but not on one that refused it firmly
    // (naming another home, or keeping a newer lesson: it never took it). It
    // is the lesson here then, ended (no lesson on that no PC follows), and
    // the pollers hand it only to a PC still in it; every PC's line names that
    // PC, which this PC has now seen.
    // Null otherwise, as for a refusal naming an older home (a PC still in
    // that home's lesson). With the lesson lock held, the lesson just adopted.
    private async Task<IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)>?> BackOutForANewerHomeAsync(
        ClassLesson lesson, IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)> lines, long generation)
    {
        var refusing = lines.Where(line => !line.Ok && line.Detail.StartsWith(ClassHomeClaim.OtherHomeText, StringComparison.Ordinal))
            .Select(line => line.Machine).ToList();
        if (refusing.Count == 0)
        {
            return null;
        }
        var homes = await Task.WhenAll(refusing.Select(machine => Task.Run(() => HomeOf(machine)))).ConfigureAwait(false);
        var newer = Strongest(homes.Select(read => read?.Home).OfType<ClassHome>()
            .Where(home => !IsThisPc(home, generation) && Outranks(home, generation, _machineName)));
        if (newer is null)
        {
            return null;
        }

        _log($"{newer.Name} keeps the class folders since this PC's claims: the lesson of {lesson.Turma} is taken back.");
        var ended = HeldEndOf(lesson);
        var refused = lines.Where(line => !line.Ok && (line.Detail.StartsWith(ClassHomeClaim.OtherHomeText, StringComparison.Ordinal)
                || line.Detail.StartsWith(ClassLesson.KeptNewerText, StringComparison.Ordinal)))
            .Select(line => Key(line.Machine)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        await SendHeldEndAsync(ended, await LessonsOnTheAgentsAsync(_stop.Token).ConfigureAwait(false), refused).ConfigureAwait(false);
        RememberOtherHome(newer);
        RaiseSavedAtFloor(ended.SavedAt);
        EndUncollected(lesson);
        Volatile.Write(ref _heldEnd, ended);
        Adopt(ended);
        return EveryPc(newer.RefusalText);
    }

    // Whether home keeps the room over the PC with that generation and
    // computer name, as every teacher PC decides it (ClassHomeTracker.Outranks).
    private static bool Outranks(ClassHome home, long generation, string name) => ClassHomeTracker.Outranks(home, generation, name);

    private static ClassHome? Strongest(IEnumerable<ClassHome> homes) => ClassHomeTracker.Strongest(homes);
}
