using System.Drawing;

namespace TreezeSuite.Session;

public interface IScreenCapture
{
    Bitmap CaptureFrame();
}
