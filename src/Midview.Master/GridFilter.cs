namespace Midview.Master;

// "Só online": which tiles the classroom grid shows, and what it says when
// that leaves none. Only the view changes -- the whole-class actions and the
// machine counts still cover every machine.
public static class GridFilter
{
    // A machine not heard from yet shows up as soon as it answers.
    public static bool Shows(MachineTile tile, bool onlineOnly) =>
        !onlineOnly || tile.Presence == TilePresence.Online;

    // Shown in the grid instead of leaving it blank; null when something
    // shows, or when there are no machines at all (the classroom's own hint).
    public static string? EmptyNotice(IReadOnlyCollection<MachineTile> tiles, bool onlineOnly)
    {
        if (!onlineOnly || tiles.Count == 0 || tiles.Any(tile => Shows(tile, onlineOnly)))
        {
            return null;
        }
        return tiles.Any(tile => tile.Presence == TilePresence.Connecting)
            ? "Procurando as máquinas ligadas..."
            : "Nenhuma máquina online agora: todas estão desligadas ou sem resposta.";
    }
}
