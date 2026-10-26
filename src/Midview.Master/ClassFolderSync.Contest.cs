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
        if (answers.Any(answer => !answer.Ok && answer.Detail == ClassFolderText.EndLessonFirst))
        {
            _log($"{rival.Name}'s lesson is on before this PC took its PCs back: nothing starts here until it ends.");
            return HomeClaim.Refused(EveryPc(ClassFolderText.OtherPcsLessonFirst(rival.Name.Length > 0 ? rival.Name : ClassHome.UnnamedText)));
        }
        return new HomeClaim(
            null, busy.Union(answers.Where(answer => answer.Busy).Select(answer => Key(answer.Machine))).ToHashSet(), generation);
    }

    // After a start's lesson went to the Agents: one that refused it for a
    // home that outranks this PC (it claimed the room meanwhile, on PCs this
    // PC didn't claim: off for this PC, say) means the room is that PC's.
    // The lesson is ended as a held lesson's end (SendHeldEndAsync: one second
    // after it, never newer than that PC's lesson of the same moment): on the
    // Agents still in it, and on those that can't be read (one whose answer
    // was lost may have taken it). It is the lesson here then, ended (no
    // lesson on that no PC follows), and the pollers hand it only to a PC
    // still in it; every PC's line names that PC, which this PC has now seen.
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
        await SendHeldEndAsync(ended, await LessonsOnTheAgentsAsync(_stop.Token).ConfigureAwait(false)).ConfigureAwait(false);
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

    // The home that outranks every other one of homes; null for none.
    private static ClassHome? Strongest(IEnumerable<ClassHome> homes) =>
        homes.Aggregate((ClassHome?)null, (best, home) => best is null || Outranks(home, best.Generation, best.Name) ? home : best);
}
