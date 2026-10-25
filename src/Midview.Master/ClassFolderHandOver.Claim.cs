using System.IO;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderHandOver: the room's Agents taking this PC as the class folders'
// home (Ruling 9). Every online Agent is claimed with a take-over and a
// generation newer than now and than any home they keep. An Agent's answer can
// be lost after it took the claim (its lesson lock held it past the read
// timeout, a network blip): so when no answer said yes and some didn't come,
// the Agents are asked which home they keep before anything is put back, and
// one that keeps this PC at that generation took it.
public sealed partial class ClassFolderHandOver
{
    // How a claim reaches a machine's Agent: ClassFolderClient.ClaimHome when
    // null. A test's stand-in for an Agent whose answer gets lost.
    internal Func<KnownMachine, ClassHomeClaim, (bool Ok, string Detail, bool OldAgent)>? SendClaim { get; set; }

    private enum Answer
    {
        Took,
        Refused,

        // An Agent from before two teacher PCs: it keeps no home.
        Old,

        // No answer came: it may have taken the claim.
        Lost,
    }

    // Ok when at least one Agent took it (the others are taken back at this
    // PC's next start); otherwise why not, as an Agent said it (a lesson on), or
    // that none answered. Generation: the one claimed.
    private (bool Ok, string Detail, long Generation) Claim()
    {
        var online = AgentsOnline();
        var homes = Homes(online);
        var generation = ClassHomeFileMaster.TakeOverGeneration(new DateTimeOffset(_localNow()).UtcTicks, homes);
        var claim = new ClassHomeClaim(_machineName, _homePort, TakeOver: true, generation);
        var answers = AskEach(online, machine => ClaimOn(machine, claim));
        foreach (var refused in answers.Where(answer => answer.Answer == Answer.Refused))
        {
            _log($"{refused.Machine.Name} didn't take this PC as the class folders' home: {refused.Detail}");
        }

        if (answers.Any(answer => answer.Answer == Answer.Took))
        {
            return (true, string.Empty, generation);
        }
        if (answers.Any(answer => answer.Answer == Answer.Lost)
            && Homes(online).Any(home => IsThisPcAt(home, generation)))
        {
            _log("An Agent whose answer was lost took this PC as the class folders' home.");
            return (true, string.Empty, generation);
        }
        var why = answers.Where(answer => answer.Answer == Answer.Refused).Select(answer => answer.Detail)
            .FirstOrDefault(detail => detail.Length > 0);
        return (false, why ?? ClassFolderText.NoPcTookIt, generation);
    }

    // The home an Agent keeps is this PC, at the generation it just claimed with.
    private bool IsThisPcAt(ClassHome home, long generation) =>
        home.Generation == generation
        && string.Equals(home.Thumbprint, _certificate.Thumbprint, StringComparison.OrdinalIgnoreCase)
        && string.Equals(home.Name, _machineName, StringComparison.OrdinalIgnoreCase);

    // What the machine's Agent answered the claim.
    private ClaimAnswer ClaimOn(KnownMachine machine, ClassHomeClaim claim)
    {
        try
        {
            var (ok, detail, oldAgent) = SendClaim is { } send
                ? send(machine, claim)
                : ClassFolderClient.ClaimHome(machine, _certificate, claim, _agentPort);
            return new ClaimAnswer(machine, oldAgent ? Answer.Old : ok ? Answer.Took : Answer.Refused, detail);
        }
        catch (Exception ex)
        {
            ClassLessonOnAgents.LogUnexpected(_log, ex, $"Claiming the class folders' home on {machine.Name} failed");
            return new ClaimAnswer(machine, Answer.Lost, string.Empty);
        }
    }

    // The homes the machines' Agents keep (none from one that keeps none, is
    // from before two teacher PCs, or doesn't answer).
    private List<ClassHome> Homes(IReadOnlyList<KnownMachine> machines) =>
        AskEach(machines, machine =>
        {
            try
            {
                return ClassFolderClient.GetHome(machine, _certificate, _agentPort).Home;
            }
            catch (Exception ex)
            {
                ClassLessonOnAgents.LogUnexpected(_log, ex, $"Reading the class folders' home of {machine.Name} failed");
                return null;
            }
        }).OfType<ClassHome>().ToList();

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

    // The online machines whose Agent the classroom list knows (one whose state can't be told is off).
    private List<KnownMachine> AgentsOnline()
    {
        IReadOnlyList<KnownMachine> machines;
        try
        {
            machines = _machines();
        }
        catch (Exception ex)
        {
            _log($"Reading the classroom list failed: {ex}");
            return new List<KnownMachine>();
        }
        return machines.Where(machine => machine.AgentThumbprint is { Length: > 0 } && IsOnline(machine)).ToList();
    }

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

    // Each machine at once, each on its own pool thread.
    private static T[] AskEach<T>(IReadOnlyList<KnownMachine> machines, Func<KnownMachine, T> ask) =>
        Task.WhenAll(machines.Select(machine => Task.Run(() => ask(machine)))).GetAwaiter().GetResult();

    private sealed record ClaimAnswer(KnownMachine Machine, Answer Answer, string Detail);
}
