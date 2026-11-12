namespace Midview.Master;

// The grid as moving machines to new addresses needs it (MainWindow).
public interface ITileMoveHost
{
    // Where the machine is in the grid now; -1 when it is gone.
    int IndexOf(KnownMachine machine);

    bool IsLocked(int index);

    // A new tile for the machine at the same place: a new poller at the new
    // address. The old tile's screen lock goes with it.
    void Replace(int index, KnownMachine updated);

    void Lock(int index);

    // How many phones the class lock left out (MainWindow._screenLockLeftOut):
    // forgotten when the last lock goes, as Replace may do for a moment.
    string? LockLeftOut { get; set; }
}

// Moves machines of the list to the addresses a search proved. One whose
// screen was meant to be locked is locked again at the new address:
// replacing its tile would give the screen back otherwise. A machine the
// teacher removed or edited meanwhile is left alone.
public static class TileMoves
{
    public static int Apply(ITileMoveHost host, IReadOnlyList<(KnownMachine Old, KnownMachine Updated)> moves)
    {
        var moved = 0;
        foreach (var (old, updated) in moves)
        {
            var index = host.IndexOf(old);
            if (index < 0)
            {
                continue;
            }
            var wasLocked = host.IsLocked(index);
            var leftOut = host.LockLeftOut;
            host.Replace(index, updated);
            if (wasLocked)
            {
                host.LockLeftOut = leftOut;
                host.Lock(index);
            }
            moved++;
        }
        return moved;
    }
}
