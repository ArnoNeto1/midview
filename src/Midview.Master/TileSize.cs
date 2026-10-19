using Midview.Protocol;

namespace Midview.Master;

// How big each machine's screen is drawn in the classroom grid: the
// teacher makes the tiles smaller to see more machines at once, or bigger
// to see each one better. Widths come in steps, so the slider, Ctrl+wheel
// and Ctrl +/- all land on the same few sizes; the height is always 16:9.
public static class TileSize
{
    public static IReadOnlyList<int> Widths { get; } = new[] { 160, 192, 224, 256, 320, 384, 448, 512, 640 };

    // The size tiles had before they could be resized.
    public const int Default = 256;

    public static int Smallest => Widths[0];
    public static int Largest => Widths[^1];

    public static int HeightOf(int width) => width * 9 / 16;

    public static int Larger(int width) => Widths.FirstOrDefault(step => step > width, Largest);

    public static int Smaller(int width) => Widths.LastOrDefault(step => step < width, Smallest);

    // For a width that isn't a step (a hand-edited preferences file).
    public static int Snap(int width) => Widths.MinBy(step => Math.Abs(step - width));

    // "Visualizar todos": the largest step at which every machine shown is in view
    // without scrolling, in a WrapPanel of availableWidth x availableHeight.
    // chromeWidth/chromeHeight is what a tile adds around its thumbnail
    // (border, padding, margin, the name and badges below it). When even the
    // smallest doesn't fit, the smallest: as many as possible in view.
    public static int ToFitAll(
        int machines, double availableWidth, double availableHeight, double chromeWidth, double chromeHeight)
    {
        foreach (var width in Widths.Reverse())
        {
            var columns = (int)(availableWidth / (width + chromeWidth));
            if (columns < 1)
            {
                continue;
            }
            var rows = (machines + columns - 1) / columns;
            if (rows * (HeightOf(width) + chromeHeight) <= availableHeight)
            {
                return width;
            }
        }
        return Smallest;
    }

    // The thumbnail to ask each machine for: the tile in real screen pixels
    // (dpiScale is Windows' scale, 1.25 at 125%), so it is neither blurry
    // nor bigger than shown -- up to what a Session renders.
    public static (int Width, int Height) Request(int width, double dpiScale) =>
        (Math.Min((int)Math.Ceiling(width * dpiScale), ThumbnailRequest.MaxWidth),
         Math.Min((int)Math.Ceiling(HeightOf(width) * dpiScale), ThumbnailRequest.MaxHeight));
}
