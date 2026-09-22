using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

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

    // Compares raw pixel bytes via LockBits instead of GetPixel (which was
    // confirmed live to be the pipeline's actual bottleneck -- see the
    // capture-cadence fix in SessionServer for the other half of that fix).
    // Bitmaps are always Format32bppArgb here (GdiScreenCapture's own
    // default), so no per-call format conversion happens.
    private static bool TilesEqual(Bitmap a, Bitmap b, Rectangle rect)
    {
        var aData = a.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var bData = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = rect.Width * 4;
            var aRow = new byte[rowBytes];
            var bRow = new byte[rowBytes];
            for (var y = 0; y < rect.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(aData.Scan0, y * aData.Stride), aRow, 0, rowBytes);
                Marshal.Copy(IntPtr.Add(bData.Scan0, y * bData.Stride), bRow, 0, rowBytes);
                if (!aRow.AsSpan().SequenceEqual(bRow))
                {
                    return false;
                }
            }
            return true;
        }
        finally
        {
            a.UnlockBits(aData);
            b.UnlockBits(bData);
        }
    }
}
