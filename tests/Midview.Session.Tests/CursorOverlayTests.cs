using System.Drawing;
using Midview.Session;
using Xunit;

public class CursorOverlayTests
{
    // The arrow's hot spot is its tip, at the image's corner.
    [Fact]
    public void ImagePosition_AnArrow_StartsAtTheCursor()
    {
        Assert.Equal(new Point(500, 300), CursorOverlay.ImagePosition(new Point(500, 300), new Point(0, 0), new Point(0, 0)));
    }

    // The I-beam's or the hourglass's hot spot is in its middle.
    [Fact]
    public void ImagePosition_PutsTheHotSpotOnTheCursor()
    {
        Assert.Equal(new Point(484, 284), CursorOverlay.ImagePosition(new Point(500, 300), new Point(16, 16), new Point(0, 0)));
    }

    // Frames are of the primary screen, whose corner isn't always at 0,0.
    [Fact]
    public void ImagePosition_IsOnTheFrame_NotTheDesktop()
    {
        Assert.Equal(new Point(2020, 50), CursorOverlay.ImagePosition(new Point(100, 50), new Point(0, 0), new Point(-1920, 0)));
    }

    // Whatever the cursor is doing on the machine running the tests (hidden,
    // on another desktop...), drawing it never costs the frame.
    [Fact]
    public void Draw_NeverThrows()
    {
        using var frame = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(frame);

        CursorOverlay.Draw(graphics, new Point(0, 0));
    }
}
