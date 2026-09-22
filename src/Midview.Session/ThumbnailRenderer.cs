using System.Drawing;
using System.Drawing.Drawing2D;
using Midview.Protocol;

namespace Midview.Session;

public static class ThumbnailRenderer
{
    // Lower than TileCodec's default of 90: the classroom grid shows this
    // image at a fraction of the screen's real size, where the extra
    // fidelity isn't visible but the extra bytes still cost bandwidth
    // across every machine in the lab.
    private const long JpegQuality = 75L;

    // Scales the whole frame to fit within maxWidth x maxHeight, keeping its
    // aspect ratio (a 5:4 screen isn't stretched into a 16:9 box), then
    // JPEG-encodes it as one image -- no 64px tile grid, no diffing.
    public static byte[] Render(Bitmap frame, int maxWidth, int maxHeight)
    {
        var scale = Math.Min((double)maxWidth / frame.Width, (double)maxHeight / frame.Height);
        var width = Math.Max(1, (int)Math.Round(frame.Width * scale));
        var height = Math.Max(1, (int)Math.Round(frame.Height * scale));

        using var thumbnail = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(thumbnail))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(frame, 0, 0, width, height);
        }

        return TileCodec.EncodeTile(thumbnail, new Rectangle(0, 0, width, height), JpegQuality);
    }
}
