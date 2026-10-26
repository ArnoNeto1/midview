namespace Midview.Master;

// Where a tile sits when the teacher places the tiles like the machines in
// the room ("Organizar sala"), in cells: one cell is one tile and what it
// adds around its screen, so the room keeps its shape at any tile size.
public readonly record struct TileCell(double X, double Y);

// The room's geometry, apart from the window: where tiles start out, where
// a new one goes, how a dragged one lands, and how big the room is.
public static class TileArrangement
{
    // Where the tile at this place in the rows was, so that turning
    // "Organizar sala" on leaves the grid looking as it did.
    public static TileCell FromRows(int index, int columns) =>
        new(index % Math.Max(1, columns), index / Math.Max(1, columns));

    // A machine with no place yet (new to the room): the first whole cell
    // free, row by row, in a room as wide as it already is.
    public static TileCell FirstFree(IReadOnlyCollection<TileCell> taken, int columns)
    {
        var occupied = taken.Select(Rounded).ToHashSet();
        var width = Math.Max(1, Math.Max(columns, (int)Math.Ceiling(Extent(taken).Columns)));
        for (var index = 0; ; index++)
        {
            var cell = FromRows(index, width);
            if (!occupied.Contains(cell))
            {
                return cell;
            }
        }
    }

    // Dragged off the top or the left: back to the edge, which is where the
    // room starts.
    public static TileCell Dropped(TileCell cell) => new(Math.Max(0, cell.X), Math.Max(0, cell.Y));

    // "Alinhar": every tile onto the nearest whole cell. A tile already on
    // one stays there, so aligning never pushes the rest of the room about;
    // one dragged off the cells and let go over a taken one takes the
    // nearest free cell instead. Between two of a kind, the later (in the
    // order given) gives way.
    public static IReadOnlyList<TileCell> Aligned(IReadOnlyList<TileCell> cells)
    {
        var taken = new HashSet<TileCell>();
        var aligned = new TileCell[cells.Count];
        var settledFirst = Enumerable.Range(0, cells.Count).OrderBy(index => IsWhole(cells[index]) ? 0 : 1);
        foreach (var index in settledFirst)
        {
            aligned[index] = NearestFree(cells[index], taken);
            taken.Add(aligned[index]);
        }
        return aligned;
    }

    private static bool IsWhole(TileCell cell) =>
        cell.X >= 0 && cell.Y >= 0 && cell.X == Math.Round(cell.X) && cell.Y == Math.Round(cell.Y);

    // A machine renamed in the room keeps the place the teacher gave it
    // (places are kept by name), and its old name keeps none, so a machine
    // named like that later starts from a free cell.
    public static void Renamed(IDictionary<string, TileCell> cells, string from, string to)
    {
        if (cells.Remove(from, out var cell))
        {
            cells[to] = cell;
        }
    }

    // How many cells across and down the room takes, from its top-left
    // corner; at least one of each.
    public static (double Columns, double Rows) Extent(IEnumerable<TileCell> cells)
    {
        double columns = 1, rows = 1;
        foreach (var cell in cells)
        {
            columns = Math.Max(columns, cell.X + 1);
            rows = Math.Max(rows, cell.Y + 1);
        }
        return (columns, rows);
    }

    private static TileCell Rounded(TileCell cell) =>
        new(Math.Max(0, Math.Round(cell.X)), Math.Max(0, Math.Round(cell.Y)));

    private static TileCell NearestFree(TileCell cell, HashSet<TileCell> taken)
    {
        var rounded = Rounded(cell);
        if (!taken.Contains(rounded))
        {
            return rounded;
        }
        for (var ring = 1; ; ring++)
        {
            var best = default(TileCell?);
            var bestDistance = double.MaxValue;
            for (var dy = -ring; dy <= ring; dy++)
            {
                for (var dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring)
                    {
                        continue;
                    }
                    var candidate = new TileCell(rounded.X + dx, rounded.Y + dy);
                    if (candidate.X < 0 || candidate.Y < 0 || taken.Contains(candidate))
                    {
                        continue;
                    }
                    var distance = Math.Pow(candidate.X - cell.X, 2) + Math.Pow(candidate.Y - cell.Y, 2);
                    if (distance < bestDistance)
                    {
                        best = candidate;
                        bestDistance = distance;
                    }
                }
            }
            if (best is { } found)
            {
                return found;
            }
        }
    }
}
