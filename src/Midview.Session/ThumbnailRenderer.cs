using System.Drawing;
using System.Drawing.Drawing2D;
using Midview.Protocol;

namespace Midview.Session;

// One small JPEG of the whole screen for the classroom console's grid --
// captured, downscaled and encoded once per request, with none of the
// full-control feed's tile-diffing or capture thread.
public static class ThumbnailRenderer
{
    public const int MaxWidth = 320;
    public const int MaxHeight = 180;

    public static byte[] Render(IScreenCapture capture)
    {
        using var frame = capture.CaptureFrame();
        var size = FitWithin(frame.Width, frame.Height, MaxWidth, MaxHeight);

        using var thumbnail = new Bitmap(size.Width, size.Height);
        using (var graphics = Graphics.FromImage(thumbnail))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(frame, 0, 0, size.Width, size.Height);
        }

        return TileCodec.EncodeTile(thumbnail, new Rectangle(Point.Empty, size), quality: 70L);
    }

    // Keeps the screen's aspect ratio (a 4:3 or 16:10 lab monitor would be
    // distorted by a fixed 320x180) and never scales up.
    public static Size FitWithin(int width, int height, int maxWidth, int maxHeight)
    {
        var scale = Math.Min(1.0, Math.Min((double)maxWidth / width, (double)maxHeight / height));
        return new Size(
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }
}
