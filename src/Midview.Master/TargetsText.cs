namespace Midview.Master;

// The word each action window puts before its machines: "Em" (Na turma
// toda), "De" (Da turma toda) or "Para" (Para a turma toda).
public enum TargetsPreposition
{
    In,
    From,
    To,
}

// The line at the top of an action window saying which machines it acts on.
// One machine is named; the whole class (every machine in the classroom
// list) is "turma toda"; a few picked on the grid are counted and named, so
// the window never says "turma toda" for only some of them.
public static class TargetsText
{
    // Names this many machines of a few picked on the grid, then counts the rest.
    private const int Named = 5;

    public static string Describe(TargetsPreposition preposition, IReadOnlyList<string> names, bool wholeClass)
    {
        var (single, whole) = preposition switch
        {
            TargetsPreposition.From => ("De", "Da turma toda"),
            TargetsPreposition.To => ("Para", "Para a turma toda"),
            _ => ("Em", "Na turma toda"),
        };

        if (names.Count == 1)
        {
            return $"{single}: {names[0]}";
        }
        if (wholeClass)
        {
            return $"{whole} ({names.Count} máquinas)";
        }

        var named = string.Join(", ", names.Take(Named));
        var rest = names.Count > Named ? $" e mais {names.Count - Named}" : string.Empty;
        return $"{single} {names.Count} máquinas: {named}{rest}";
    }

    public static string Describe(TargetsPreposition preposition, IReadOnlyList<KnownMachine> targets, bool wholeClass) =>
        Describe(preposition, targets.Select(machine => machine.Name).ToList(), wholeClass);
}
