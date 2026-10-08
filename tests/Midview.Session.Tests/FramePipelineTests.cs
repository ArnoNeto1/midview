using System.Drawing;
using Midview.Session;
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
    public void CaptureNextFrame_FirstCall_ReturnsAllTilesEncoded()
    {
        var frame = SolidBitmap(128, 128, Color.Red);
        var pipeline = new FramePipeline(new FakeScreenCapture(frame));

        var tiles = pipeline.CaptureNextFrame();

        Assert.Equal(4, tiles.Count); // 128x128 is a 2x2 grid of 64x64 tiles
        Assert.All(tiles, tile => Assert.NotEmpty(tile.JpegBytes));
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

    [Fact]
    public void CaptureNextFrame_ResolutionChanged_ReturnsAllTilesOfTheNewSize()
    {
        var frame1 = SolidBitmap(128, 128, Color.Red);
        var frame2 = SolidBitmap(192, 64, Color.Red);
        var pipeline = new FramePipeline(new FakeScreenCapture(frame1, frame2));
        pipeline.CaptureNextFrame();

        var tiles = pipeline.CaptureNextFrame();

        Assert.Equal(3, tiles.Count); // 192x64 is a 3x1 grid of 64x64 tiles
    }

    [Fact]
    public void CaptureNextUpdate_FirstCall_IsTheWholeScreenAsOneRegion()
    {
        var pipeline = new FramePipeline(new FakeScreenCapture(SolidBitmap(200, 100, Color.Red)));

        var update = pipeline.CaptureNextUpdate();

        Assert.Equal((200, 100), (update.Width, update.Height));
        var region = Assert.Single(update.Regions);
        Assert.Equal((0, 0, 200, 100), (region.X, region.Y, region.Width, region.Height));
        using var decoded = Midview.Protocol.TileCodec.DecodeTile(region.Jpeg);
        Assert.Equal((200, 100), (decoded.Width, decoded.Height));
    }

    [Fact]
    public void CaptureNextUpdate_NothingChanged_HasNoRegions()
    {
        var pipeline = new FramePipeline(new FakeScreenCapture(
            SolidBitmap(128, 128, Color.Red), SolidBitmap(128, 128, Color.Red)));
        pipeline.CaptureNextUpdate();

        var update = pipeline.CaptureNextUpdate();

        Assert.Equal((128, 128), (update.Width, update.Height));
        Assert.Empty(update.Regions);
    }

    [Fact]
    public void CaptureNextUpdate_OneTileChanged_IsThatTile()
    {
        var frame2 = SolidBitmap(640, 320, Color.Red);
        using (var g = Graphics.FromImage(frame2))
        {
            g.FillRectangle(Brushes.Blue, 64, 128, 64, 64);
        }
        var pipeline = new FramePipeline(new FakeScreenCapture(SolidBitmap(640, 320, Color.Red), frame2));
        pipeline.CaptureNextUpdate();

        var update = pipeline.CaptureNextUpdate();

        var region = Assert.Single(update.Regions);
        Assert.Equal((64, 128, 64, 64), (region.X, region.Y, region.Width, region.Height));
    }

    [Fact]
    public void ReusableCapture_DrawsIntoTheFrameBeforeLast_AndStillSeesWhatChanged()
    {
        var capture = new ReusingScreenCapture(
            (128, 128, Color.Red), (128, 128, Color.Red), (128, 128, Color.Blue), (128, 128, Color.Blue));
        var pipeline = new FramePipeline(capture);

        Assert.Single(pipeline.CaptureNextUpdate().Regions); // red: everything is new
        Assert.Empty(pipeline.CaptureNextUpdate().Regions); // red again
        Assert.Single(pipeline.CaptureNextUpdate().Regions); // blue, into the first frame's bitmap
        Assert.Empty(pipeline.CaptureNextUpdate().Regions); // blue again, into the second's

        Assert.Equal(2, capture.NewBitmaps);
        Assert.Equal(2, capture.Reused);
    }

    // A projector plugged in, display settings changed: the bitmaps kept for
    // reuse are the old size, so the next frame must be captured fresh.
    [Fact]
    public void ReusableCapture_ResolutionChanged_CapturesANewBitmapOfTheNewSize()
    {
        var capture = new ReusingScreenCapture(
            (128, 128, Color.Red), (128, 128, Color.Red), (192, 64, Color.Blue), (192, 64, Color.Blue));
        var pipeline = new FramePipeline(capture);
        pipeline.CaptureNextUpdate();
        pipeline.CaptureNextUpdate();

        var resized = pipeline.CaptureNextUpdate();
        var still = pipeline.CaptureNextUpdate();

        Assert.Equal((192, 64), (resized.Width, resized.Height));
        var region = Assert.Single(resized.Regions);
        Assert.Equal((0, 0, 192, 64), (region.X, region.Y, region.Width, region.Height));
        Assert.Equal((192, 64), (still.Width, still.Height));
        Assert.Empty(still.Regions);
        Assert.Equal(4, capture.NewBitmaps); // nothing of the new size to reuse yet
    }

    // Each frame is a size and a colour. Like GdiScreenCapture, it only
    // captures into a bitmap of the screen's current size.
    private sealed class ReusingScreenCapture : IReusableScreenCapture
    {
        private readonly Queue<(int Width, int Height, Color Color)> _frames;

        public ReusingScreenCapture(params (int Width, int Height, Color Color)[] frames)
        {
            _frames = new Queue<(int Width, int Height, Color Color)>(frames);
        }

        public int NewBitmaps { get; private set; }
        public int Reused { get; private set; }

        public Bitmap CaptureFrame()
        {
            NewBitmaps++;
            var (width, height, color) = _frames.Dequeue();
            return SolidBitmap(width, height, color);
        }

        public bool TryCaptureInto(Bitmap target)
        {
            var (width, height, color) = _frames.Peek();
            if (target.Width != width || target.Height != height)
            {
                return false;
            }

            _frames.Dequeue();
            Reused++;
            using var g = Graphics.FromImage(target);
            g.Clear(color);
            return true;
        }
    }

    private static Bitmap SolidBitmap(int width, int height, Color color)
    {
        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(color);
        return bitmap;
    }
}
