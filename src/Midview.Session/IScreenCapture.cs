using System.Drawing;

namespace Midview.Session;

public interface IScreenCapture
{
    Bitmap CaptureFrame();
}

// A capture that can draw into the bitmap of an earlier frame instead of
// allocating a new one every time: a fresh full-screen bitmap cost about
// 5 ms a frame in page faults alone.
public interface IReusableScreenCapture : IScreenCapture
{
    // False, leaving target as it was, when target isn't the screen's size
    // (the resolution changed): the caller then uses CaptureFrame.
    bool TryCaptureInto(Bitmap target);
}
