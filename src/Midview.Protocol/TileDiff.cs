using System.Drawing;

namespace Midview.Protocol;

public static class TileDiff
{
    public const int TileSize = 64;

    public static IReadOnlyList<TileCoord> FindChangedTiles(Bitmap previous, Bitmap current)
    {
        if (previous.Width != current.Width || previous.Height != current.Height)
        {
            throw new ArgumentException("Bitmaps must be the same size to diff");
        }

        var changed = new List<TileCoord>();
        var columns = (int)Math.Ceiling(current.Width / (double)TileSize);
        var rows = (int)Math.Ceiling(current.Height / (double)TileSize);

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var rect = TileRect(row, column, current.Width, current.Height);
                if (!TilesEqual(previous, current, rect))
                {
                    changed.Add(new TileCoord(row, column));
                }
            }
        }

        return changed;
    }

    public static IReadOnlyList<TileCoord> AllTiles(int width, int height)
    {
        var columns = (int)Math.Ceiling(width / (double)TileSize);
        var rows = (int)Math.Ceiling(height / (double)TileSize);

        var tiles = new List<TileCoord>(rows * columns);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                tiles.Add(new TileCoord(row, column));
            }
        }

        return tiles;
    }

    public static Rectangle TileRect(int row, int column, int imageWidth, int imageHeight)
    {
        var x = column * TileSize;
        var y = row * TileSize;
        var width = Math.Min(TileSize, imageWidth - x);
        var height = Math.Min(TileSize, imageHeight - y);
        return new Rectangle(x, y, width, height);
    }

    // NOTE: GetPixel-based comparison is correct but slow (unsuitable for a
    // final low-latency pipeline). This is intentional for a first working
    // version -- switch to LockBits + unsafe pointer scanning once the rest
    // of the pipeline works end-to-end and this is confirmed as the
    // bottleneck, not before (YAGNI).
    private static bool TilesEqual(Bitmap a, Bitmap b, Rectangle rect)
    {
        for (var y = rect.Top; y < rect.Bottom; y++)
        {
            for (var x = rect.Left; x < rect.Right; x++)
            {
                if (a.GetPixel(x, y) != b.GetPixel(x, y))
                {
                    return false;
                }
            }
        }
        return true;
    }
}
