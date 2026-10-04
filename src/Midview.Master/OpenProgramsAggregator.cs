using Midview.Protocol;

namespace Midview.Master;

// One program to offer in the block window, gathered from what several
// machines reported open: its exe name, a name to show, and how many
// machines have it open.
public sealed record OpenProgramRow(string ExeName, string DisplayName, int MachineCount);

// Turns each machine's OpenPrograms answer into one list for the picker.
// Pure, so it can be tested. A student can forge what their machine reports,
// so the display name for an exe is the one most machines agree on, and the
// exe name is always shown too.
public static class OpenProgramsAggregator
{
    public static IReadOnlyList<OpenProgramRow> Aggregate(
        IEnumerable<IReadOnlyList<OpenProgram>> answersPerMachine)
    {
        var machineCount = new Dictionary<string, int>(StringComparer.Ordinal);
        var displayVotes = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        foreach (var answer in answersPerMachine)
        {
            foreach (var exeName in answer.Select(program => program.ExeName).Distinct(StringComparer.Ordinal))
            {
                machineCount[exeName] = machineCount.GetValueOrDefault(exeName) + 1;
            }
            foreach (var program in answer)
            {
                var votes = displayVotes.TryGetValue(program.ExeName, out var existing)
                    ? existing
                    : displayVotes[program.ExeName] = new Dictionary<string, int>(StringComparer.Ordinal);
                votes[program.DisplayName] = votes.GetValueOrDefault(program.DisplayName) + 1;
            }
        }

        return machineCount
            .Select(entry => new OpenProgramRow(entry.Key, MostAgreed(displayVotes[entry.Key], entry.Key), entry.Value))
            .OrderByDescending(row => row.MachineCount)
            .ThenBy(row => row.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // The most reported name; ties broken by ordinal order so the result is
    // stable and one forged label can't win.
    private static string MostAgreed(Dictionary<string, int> votes, string exeName) =>
        votes.Count == 0
            ? exeName
            : votes.OrderByDescending(vote => vote.Value).ThenBy(vote => vote.Key, StringComparer.Ordinal).First().Key;
}
