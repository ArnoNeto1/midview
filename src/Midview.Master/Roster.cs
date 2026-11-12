using System.Net;

namespace Midview.Master;

// The classroom-list changes that pairing and searching make, worked out
// on the list; MainWindow applies them to its tiles.
public static class Roster
{
    // Where a newly paired machine goes: in place of the entry with the
    // same Agent (it paired again), else of the one with the same name (it
    // was reinstalled), else in classroom order (Aluno-2 before Aluno-10).
    // A network card learned before is kept if the new entry has none.
    public static (int Index, bool Replaces, KnownMachine Machine) Place(IReadOnlyList<KnownMachine> roster, KnownMachine paired)
    {
        var index = IndexOf(roster, entry => SameAgent(entry, paired.AgentThumbprint));
        if (index < 0)
        {
            index = IndexOf(roster, entry => string.Equals(entry.Name, paired.Name, StringComparison.OrdinalIgnoreCase));
        }
        if (index >= 0)
        {
            return (index, true, paired.KeepingMacAddressOf(roster[index]));
        }

        var position = IndexOf(roster, entry => NaturalOrder.Instance.Compare(entry.Name, paired.Name) > 0);
        return (position < 0 ? roster.Count : position, false, paired);
    }

    // Whether applying what Place worked out would change the list: false
    // when the machine is already there exactly as given (a colleague's class
    // joined again). Place already kept the network card learned, so a plain
    // comparison tells. MainWindow then leaves that machine's tile alone:
    // replacing it would stop its poller and give a locked screen back.
    public static bool Changes(IReadOnlyList<KnownMachine> roster, (int Index, bool Replaces, KnownMachine Machine) placed) =>
        !placed.Replaces || roster[placed.Index] != placed.Machine;

    // Machines that answered a search from another address than the list
    // has, matched by Agent thumbprint. An answer is only a hint: connecting
    // still checks the pinned thumbprint, so a false one can only make a
    // machine unreachable. Entries kept by IP address only: one kept by
    // computer name stays as the teacher typed it.
    public static IReadOnlyList<(int Index, KnownMachine Updated)> NewAddresses(
        IReadOnlyList<KnownMachine> roster, IReadOnlyList<FoundMachine> found)
    {
        var changes = new List<(int, KnownMachine)>();
        for (var i = 0; i < roster.Count; i++)
        {
            var entry = roster[i];
            if (!MovesBySearch(entry))
            {
                continue;
            }

            var answer = found.FirstOrDefault(machine => SameAgent(entry, machine.AgentThumbprint));
            if (answer is not null && !string.Equals(answer.Address, entry.HostAddress, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add((i, entry with { HostAddress = answer.Address }));
            }
        }
        return changes;
    }

    // The search the Master makes by itself (AddressSearchSchedule) moves
    // only the machines it looked for (AddressCheck.MayHaveMoved): one
    // answering at its address stays put.
    public static IReadOnlyList<(int Index, KnownMachine Updated)> NewAddressesWhere(
        IReadOnlyList<KnownMachine> roster, IReadOnlyList<FoundMachine> found, Func<int, bool> lookedFor) =>
        NewAddresses(roster, found).Where(change => lookedFor(change.Index)).ToList();

    // Whether a search can give this entry a new address: kept by IP
    // address, with its Agent known (what an answer is matched by).
    public static bool MovesBySearch(KnownMachine entry) =>
        entry.AgentThumbprint is not null && IPAddress.TryParse(entry.HostAddress, out _);

    private static bool SameAgent(KnownMachine entry, string? agentThumbprint) =>
        entry.AgentThumbprint is not null && agentThumbprint is not null
        && string.Equals(entry.AgentThumbprint, agentThumbprint, StringComparison.OrdinalIgnoreCase);

    private static int IndexOf(IReadOnlyList<KnownMachine> roster, Func<KnownMachine, bool> match)
    {
        for (var i = 0; i < roster.Count; i++)
        {
            if (match(roster[i]))
            {
                return i;
            }
        }
        return -1;
    }
}
