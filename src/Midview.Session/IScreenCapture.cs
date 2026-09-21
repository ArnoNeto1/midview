using System.Drawing;

namespace Midview.Session;

public interface IScreenCapture
{
    Bitmap CaptureFrame();
}
