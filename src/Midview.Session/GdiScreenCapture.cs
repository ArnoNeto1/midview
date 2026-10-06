using System.Drawing;
using System.Windows.Forms;

namespace Midview.Session;

public sealed class GdiScreenCapture : IScreenCapture
{
    public Bitmap CaptureFrame()
    {
        var bounds = Screen.PrimaryScreen!.Bounds;
        var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        CursorOverlay.Draw(graphics, bounds.Location);
        return bitmap;
    }
}
