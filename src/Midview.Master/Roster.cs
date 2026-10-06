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
            if (!IPAddress.TryParse(entry.HostAddress, out _))
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
