using System.Drawing;

namespace Midview.Protocol;

// Turns the changed 64x64 tiles of a frame into a few rectangles, each sent
// as one JPEG (ScreenFrame). Every JPEG carries about 600 bytes of headers
// and tables, and decoding one has a fixed cost too, so one JPEG per tile
// spent more than half of a full-screen update on overhead.
public static class TileRegions
{
    // When the changed tiles of a group fill at least this share of the area
    // around them, that whole area goes as one JPEG: the unchanged tiles
    // inside it cost less than the extra headers and decodes would.
    private const double WholeAreaShare = 0.5;

    // Changed tiles at most this many tiles apart (one unchanged tile between
    // them) belong to the same group. Groups further apart never share a
    // JPEG: a video in one corner and the mouse moving in the other would
    // otherwise send everything between them too.
    private const int GroupReach = 2;

    // No JPEG bigger than this (about 4 megapixels), so even an 8K screen's
    // update stays a few MB a JPEG; a bigger rectangle goes in bands. A whole
    // 1080p or 1440p screen still fits in one.
    private const int MaxRegionTiles = 1024;

    public static IReadOnlyList<Rectangle> Merge(IReadOnlyList<TileCoord> tiles, int imageWidth, int imageHeight)
    {
        return Groups(tiles.Distinct().ToList())
            .SelectMany(MergeGroup)
            .SelectMany(Bands)
            .Select(span => ToPixels(span, imageWidth, imageHeight))
            .ToList();
    }

    private static IEnumerable<TileSpan> MergeGroup(IReadOnlyList<TileCoord> tiles)
    {
        var top = tiles.Min(tile => tile.Row);
        var bottom = tiles.Max(tile => tile.Row);
        var left = tiles.Min(tile => tile.Column);
        var right = tiles.Max(tile => tile.Column);
        var areaTiles = (bottom - top + 1) * (right - left + 1);
        if (tiles.Count >= areaTiles * WholeAreaShare)
        {
            return new[] { new TileSpan(top, left, bottom, right) };
        }

        // Runs of side-by-side tiles in each row; a run that lines up exactly
        // with one ending on the row above makes that rectangle taller
        // instead of starting a new one.
        var spans = new List<TileSpan>();
        var endingAbove = new Dictionary<(int Left, int Right), int>();
        foreach (var row in tiles.GroupBy(tile => tile.Row).OrderBy(group => group.Key))
        {
            var columns = row.Select(tile => tile.Column).OrderBy(column => column).ToList();
            var endingHere = new Dictionary<(int Left, int Right), int>();
            var runStart = 0;
            for (var i = 1; i <= columns.Count; i++)
            {
                if (i < columns.Count && columns[i] == columns[i - 1] + 1)
                {
                    continue;
                }

                var run = (Left: columns[runStart], Right: columns[i - 1]);
                if (endingAbove.TryGetValue(run, out var index) && spans[index].Bottom == row.Key - 1)
                {
                    spans[index] = spans[index] with { Bottom = row.Key };
                }
                else
                {
                    spans.Add(new TileSpan(row.Key, run.Left, row.Key, run.Right));
                    index = spans.Count - 1;
                }
                endingHere[run] = index;
                runStart = i;
            }
            endingAbove = endingHere;
        }
        return spans;
    }

    // The changed tiles split into groups of tiles near each other, in the
    // order of each group's first tile (top to bottom, left to right).
    private static IEnumerable<IReadOnlyList<TileCoord>> Groups(List<TileCoord> tiles)
    {
        var ordered = tiles.OrderBy(tile => tile.Row).ThenBy(tile => tile.Column).ToList();
        var remaining = new HashSet<TileCoord>(ordered);
        foreach (var start in ordered)
        {
            if (!remaining.Remove(start))
            {
                continue;
            }

            var group = new List<TileCoord> { start };
            for (var i = 0; i < group.Count; i++)
            {
                for (var row = group[i].Row - GroupReach; row <= group[i].Row + GroupReach; row++)
                {
                    for (var column = group[i].Column - GroupReach; column <= group[i].Column + GroupReach; column++)
                    {
                        var near = new TileCoord(row, column);
                        if (remaining.Remove(near))
                        {
                            group.Add(near);
                        }
                    }
                }
            }
            yield return group;
        }
    }

    private static IEnumerable<TileSpan> Bands(TileSpan span)
    {
        var columns = span.Right - span.Left + 1;
        var rowsPerBand = Math.Max(1, MaxRegionTiles / columns);
        for (var top = span.Top; top <= span.Bottom; top += rowsPerBand)
        {
            yield return span with { Top = top, Bottom = Math.Min(span.Bottom, top + rowsPerBand - 1) };
        }
    }

    private static Rectangle ToPixels(TileSpan span, int imageWidth, int imageHeight)
    {
        var x = span.Left * TileDiff.TileSize;
        var y = span.Top * TileDiff.TileSize;
        var width = Math.Min((span.Right + 1) * TileDiff.TileSize, imageWidth) - x;
        var height = Math.Min((span.Bottom + 1) * TileDiff.TileSize, imageHeight) - y;
        return new Rectangle(x, y, width, height);
    }

    // Inclusive, in tiles.
    private readonly record struct TileSpan(int Top, int Left, int Bottom, int Right);
}
