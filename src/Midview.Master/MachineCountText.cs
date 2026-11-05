namespace Midview.Master;

// The classroom window's title bar chip: how many of the room's machines
// answer ("16 de 18 ligadas"). Empty while the room has no machines, so the
// chip hides.
public static class MachineCountText
{
    public static string Online(int online, int total) =>
        total == 0 ? string.Empty : $"{online} de {total} {(online == 1 ? "ligada" : "ligadas")}";
}
