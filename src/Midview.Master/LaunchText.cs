namespace Midview.Master;

// How one machine answered "Abrir site" or "Abrir programa".
public sealed record LaunchResult(string Name, bool Succeeded, string Detail);

// What the teacher reads after opening a site or programs: a line per
// machine in the window, one line in the status bar after a click in the
// menu.
public static class LaunchText
{
    // The status bar names this many machines that failed, then counts the rest.
    private const int FailuresNamed = 5;

    // One machine's answers to several commands, as its one result.
    public static LaunchResult Combine(string machine, IReadOnlyList<(string Command, bool Succeeded, string Detail)> answers) =>
        answers.Count == 1
            ? new LaunchResult(machine, answers[0].Succeeded, answers[0].Detail)
            : new LaunchResult(machine, answers.All(answer => answer.Succeeded),
                string.Join("; ", answers.Select(answer => $"{answer.Command}: {answer.Detail}")));

    public static string Summary(string label, IReadOnlyList<LaunchResult> results)
    {
        if (results.Count == 1)
        {
            var only = results[0];
            return only.Succeeded ? $"{label}: aberto em {only.Name}." : $"{label}: não abriu em {only.Name} ({only.Detail}).";
        }

        var failures = results.Where(result => !result.Succeeded).ToList();
        if (failures.Count == 0)
        {
            return $"{label}: aberto nas {results.Count} máquinas.";
        }

        var named = string.Join("; ", failures.Take(FailuresNamed).Select(failure => $"{failure.Name} ({failure.Detail})"));
        var rest = failures.Count > FailuresNamed ? $" e mais {failures.Count - FailuresNamed}" : string.Empty;
        return $"{label}: aberto em {results.Count - failures.Count} de {results.Count}. Não abriu em {named}{rest}.";
    }

    public static string Lines(IReadOnlyList<LaunchResult> results) =>
        string.Join("\n", results.Select(result => $"{(result.Succeeded ? "✓" : "✗")} {result.Name}: {result.Detail}"));
}
