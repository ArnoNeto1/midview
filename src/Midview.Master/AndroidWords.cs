namespace Midview.Master;

// What the windows call the phones and tablets among their targets, by
// their names ("Tablet 04", "Celular 07"; one paired by code counts as a
// phone): "tablet" when they are all tablets, "celular" when all phones,
// both words when mixed.
public static class AndroidWords
{
    private static IReadOnlyList<AndroidDeviceKind> Kinds(IEnumerable<KnownMachine> targets) =>
        PhoneDefaultsFile.KindsOf(targets.Where(machine => machine.IsPhone).Select(machine => machine.Name));

    // "tablet", "celular" or "celular ou tablet".
    public static string One(IEnumerable<KnownMachine> targets) => Kinds(targets) switch
    {
        { Count: > 1 } => "celular ou tablet",
        var kinds => DeviceSetupText.Word(kinds[0]),
    };

    // "tablets", "celulares" or "celulares e tablets".
    public static string Many(IEnumerable<KnownMachine> targets) => Kinds(targets) switch
    {
        { Count: > 1 } => "celulares e tablets",
        var kinds => DeviceSetupText.Words(kinds[0]),
    };
}
