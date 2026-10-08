using System.Drawing;
using System.Windows.Forms;

namespace Midview.Session;

public sealed class GdiScreenCapture : IReusableScreenCapture
{
    public Bitmap CaptureFrame()
    {
        var bounds = Screen.PrimaryScreen!.Bounds;
        var bitmap = new Bitmap(bounds.Width, bounds.Height);
        Draw(bitmap, bounds);
        return bitmap;
    }

    public bool TryCaptureInto(Bitmap target)
    {
        var bounds = Screen.PrimaryScreen!.Bounds;
        if (target.Width != bounds.Width || target.Height != bounds.Height)
        {
            return false;
        }

        Draw(target, bounds);
        return true;
    }

    private static void Draw(Bitmap bitmap, Rectangle bounds)
    {
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        CursorOverlay.Draw(graphics, bounds.Location);
    }
}
