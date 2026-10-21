using Midview.Protocol;

namespace Midview.Master;

// How big each machine's screen is drawn in the classroom grid: the
// teacher makes the tiles smaller to see more machines at once, or bigger
// to see each one better, anywhere between Smallest and Largest; the
// height is always 16:9. In rows the tiles widen to share what a row has
// left over (ToFillRow), so the size chosen is really how many fit across.
public static class TileSize
{
    // The size tiles had before they could be resized.
    public const int Default = 256;

    public const int Smallest = 120;
    public const int Largest = 960;

    public static int Clamp(int width) => Math.Clamp(width, Smallest, Largest);

    public static int HeightOf(int width) => width * 9 / 16;

    // How many tiles of this width fit across a row rowWidth wide, each
    // taking chromeWidth more around its screen. Always at least one.
    public static int Columns(int width, double rowWidth, double chromeWidth) =>
        Math.Max(1, (int)(rowWidth / (width + chromeWidth)));

    // The width at which exactly this many fit across, sharing the whole row.
    public static int ForColumns(int columns, double rowWidth, double chromeWidth) =>
        (int)(rowWidth / Math.Max(1, columns) - chromeWidth);

    // How wide the tiles are drawn at this width in a row rowWidth wide: as
    // many across as fit at the width, widened to share what the row has
    // left over, so the grid has no empty strip down its right. Never
    // narrower than the width (a row too narrow for one tile, or none laid
    // out yet).
    public static int ToFillRow(int width, double rowWidth, double chromeWidth) =>
        Math.Max(width, ForColumns(Columns(width, rowWidth, chromeWidth), rowWidth, chromeWidth));

    // One step bigger or smaller. In rows (rowWidth > 0) that is one tile
    // fewer or more across, since in between the tiles look the same; tiles
    // the teacher placed by hand (rowWidth 0) grow or shrink by an eighth.
    public static int Larger(int width, double rowWidth, double chromeWidth)
    {
        if (rowWidth <= 0)
        {
            return Clamp((int)Math.Round(width * 1.125));
        }
        var columns = Columns(width, rowWidth, chromeWidth);
        return columns <= 1 ? Largest : Clamp(ForColumns(columns - 1, rowWidth, chromeWidth));
    }

    public static int Smaller(int width, double rowWidth, double chromeWidth)
    {
        if (rowWidth <= 0)
        {
            return Clamp((int)Math.Round(width / 1.125));
        }
        return Clamp(ForColumns(Columns(width, rowWidth, chromeWidth) + 1, rowWidth, chromeWidth));
    }

    // "Visualizar todos" in rows: the largest width at which every machine
    // shown, drawn as wide as ToFillRow makes it, is in view without
    // scrolling, in a grid of availableWidth x availableHeight. Since a row
    // shares its whole width, that is the fewest tiles across whose rows
    // fit the height -- even more across than there are machines, when one
    // row of them at full width would be too tall. chromeWidth/chromeHeight
    // is what a tile adds around its screen (border, margin, the badges
    // below it). When even the smallest doesn't fit, the smallest: as many
    // as possible in view.
    public static int ToFitAll(
        int machines, double availableWidth, double availableHeight, double chromeWidth, double chromeHeight)
    {
        for (var columns = 1; ; columns++)
        {
            var width = ForColumns(columns, availableWidth, chromeWidth);
            if (width < Smallest)
            {
                break;
            }
            var rows = (machines + columns - 1) / columns;
            if (rows * (HeightOf(width) + chromeHeight) <= availableHeight)
            {
                return Clamp(width);
            }
        }
        return Smallest;
    }

    // "Visualizar todos" with the tiles placed by hand: the largest width at
    // which a room columns x rows cells big (TileArrangement.Extent) is in
    // view, each cell a tile and what it adds around its screen.
    public static int ToFitRoom(
        double columns, double rows, double availableWidth, double availableHeight, double chromeWidth, double chromeHeight)
    {
        var byWidth = availableWidth / Math.Max(1, columns) - chromeWidth;
        var byHeight = (availableHeight / Math.Max(1, rows) - chromeHeight) * 16 / 9;
        return Clamp((int)Math.Min(byWidth, byHeight));
    }

    // The thumbnail to ask each machine for: the tile in real screen pixels
    // (dpiScale is Windows' scale, 1.25 at 125%), so it is neither blurry
    // nor bigger than shown -- up to what a Session renders.
    public static (int Width, int Height) Request(int width, double dpiScale) =>
        (Math.Min((int)Math.Ceiling(width * dpiScale), ThumbnailRequest.MaxWidth),
         Math.Min((int)Math.Ceiling(HeightOf(width) * dpiScale), ThumbnailRequest.MaxHeight));
}
