using System.Drawing;
using TreezeSuite.Session;
using Xunit;

public class FakeScreenCapture : IScreenCapture
{
    private readonly Queue<Bitmap> _frames;

    public FakeScreenCapture(params Bitmap[] frames)
    {
        _frames = new Queue<Bitmap>(frames);
    }

    public Bitmap CaptureFrame() => _frames.Dequeue();
}

public class FramePipelineTests
{
    [Fact]
    public void CaptureNextFrame_FirstCall_ReturnsEmpty()
    {
        var frame = SolidBitmap(128, 128, Color.Red);
        var pipeline = new FramePipeline(new FakeScreenCapture(frame));

        var tiles = pipeline.CaptureNextFrame();

        Assert.Empty(tiles);
    }

    [Fact]
    public void CaptureNextFrame_SecondCallSameContent_ReturnsEmpty()
    {
        var frame1 = SolidBitmap(128, 128, Color.Red);
        var frame2 = SolidBitmap(128, 128, Color.Red);
        var pipeline = new FramePipeline(new FakeScreenCapture(frame1, frame2));
        pipeline.CaptureNextFrame();

        var tiles = pipeline.CaptureNextFrame();

        Assert.Empty(tiles);
    }

    [Fact]
    public void CaptureNextFrame_SecondCallOneTileChanged_ReturnsThatTileEncoded()
    {
        var frame1 = SolidBitmap(128, 128, Color.Red);
        var frame2 = SolidBitmap(128, 128, Color.Red);
        using (var g = Graphics.FromImage(frame2))
        {
            g.FillRectangle(Brushes.Blue, 64, 0, 64, 64);
        }
        var pipeline = new FramePipeline(new FakeScreenCapture(frame1, frame2));
        pipeline.CaptureNextFrame();

        var tiles = pipeline.CaptureNextFrame();

        Assert.Single(tiles);
        Assert.Equal(0, tiles[0].Row);
        Assert.Equal(1, tiles[0].Column);
        Assert.NotEmpty(tiles[0].JpegBytes);
    }

    private static Bitmap SolidBitmap(int width, int height, Color color)
    {
        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(color);
        return bitmap;
    }
}
