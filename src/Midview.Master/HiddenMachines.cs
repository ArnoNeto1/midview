namespace Midview.Master;

// "Esconder da sala": machines the teacher keeps out of sight, such as a
// notebook a teacher took away. A hidden machine is not a tile: it is out of
// the grid, the "X de Y ligadas" count and every button's reach, and nothing
// connects to it. It stays in the roster file (KnownMachine.Hidden), still
// paired, until "Telas" > "Escondidas" shows it again.
public static class HiddenMachines
{
    // The hidden machines still away from the grid: one that is back as a
    // tile (paired again, or saved by hand under the same name) is that
    // tile now, and is dropped.
    public static List<KnownMachine> StillAway(IReadOnlyCollection<KnownMachine> hidden, IReadOnlyCollection<KnownMachine> shown) =>
        hidden.Where(machine => !shown.Any(tile => IsSame(machine, tile))).ToList();

    private static bool IsSame(KnownMachine hidden, KnownMachine tile) =>
        string.Equals(hidden.Name, tile.Name, StringComparison.OrdinalIgnoreCase)
        || (hidden.AgentThumbprint is { } agent
            && string.Equals(agent, tile.AgentThumbprint, StringComparison.OrdinalIgnoreCase));
}

// What the teacher reads about the hidden machines.
public static class HiddenMachinesText
{
    public static string MenuHeader(int count) => $"Escondidas ({count})";

    public const string NoneToolTip =
        "Nenhuma máquina escondida. Para esconder uma, use o botão direito na tela dela: Esconder da sala.";

    public const string SomeToolTip = "Máquinas fora da grade e dos botões. Escolha uma para ela voltar.";

    public static string ShowHeader(string name) => $"Mostrar {name}";

    public const string ShowAllHeader = "Mostrar todas";

    // Said in the status bar after hiding.
    public static string Hidden(IReadOnlyList<string> names) => names.Count == 1
        ? $"{names[0]} escondida: saiu da grade e dos botões. Para voltar: Telas > Escondidas."
        : $"{names.Count} máquinas escondidas: saíram da grade e dos botões. Para voltar: Telas > Escondidas.";

    // Said in the status bar after showing again.
    public static string Shown(IReadOnlyList<string> names) => names.Count == 1
        ? $"{names[0]} voltou para a grade."
        : $"{names.Count} máquinas voltaram para a grade.";

    // Said in the status bar with nothing selected, so the teacher does not
    // forget a machine kept out of sight. Null with none hidden.
    public static string? Reminder(int count) => count switch
    {
        0 => null,
        1 => "1 máquina escondida (Telas > Escondidas).",
        _ => $"{count} máquinas escondidas (Telas > Escondidas).",
    };
}
