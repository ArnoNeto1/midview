using System.Collections;

namespace Midview.Master;

// The order the classroom grid shows the machines in, whatever order the
// list has them in (a machine paired later, or renamed, keeps its place in
// the list): the computers first, then the phones, then the tablets, each
// by name with numbers counted as numbers (ALUNO-2 before ALUNO-10).
// "Organizar sala" places the tiles at their cells instead.
public sealed class TileOrder : IComparer
{
    public static readonly TileOrder Instance = new();

    public int Compare(object? x, object? y)
    {
        if (x is not MachineTile a || y is not MachineTile b)
        {
            return 0;
        }
        var byKind = Group(a).CompareTo(Group(b));
        return byKind != 0 ? byKind : NaturalOrder.Instance.Compare(a.Name, b.Name);
    }

    private static int Group(MachineTile tile) =>
        !tile.Machine.IsPhone ? 0
        : DeviceSupport.IsAndroidOf(tile.Machine, AndroidDeviceKind.Tablet) ? 2
        : 1;
}
