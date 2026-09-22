using System.Drawing;
using Midview.Protocol;
using Midview.Session;
using Xunit;

public class ThumbnailRendererTests
{
    [Fact]
    public void Render_WidescreenFrame_FillsTheBoundsExactly()
    {
        using var frame = new Bitmap(1920, 1080);

        var jpegBytes = ThumbnailRenderer.Render(frame, 320, 180);

        using var thumbnail = TileCodec.DecodeTile(jpegBytes);
        Assert.Equal(320, thumbnail.Width);
        Assert.Equal(180, thumbnail.Height);
    }

    [Fact]
    public void Render_NonWidescreenFrame_KeepsAspectRatioWithinTheBounds()
    {
        using var frame = new Bitmap(1280, 1024);

        var jpegBytes = ThumbnailRenderer.Render(frame, 320, 180);

        using var thumbnail = TileCodec.DecodeTile(jpegBytes);
        Assert.Equal(225, thumbnail.Width);
        Assert.Equal(180, thumbnail.Height);
    }

    [Fact]
    public void Render_KeepsTheFramesContent()
    {
        using var frame = new Bitmap(1920, 1080);
        using (var g = Graphics.FromImage(frame))
        {
            g.Clear(Color.FromArgb(200, 30, 90));
        }

        var jpegBytes = ThumbnailRenderer.Render(frame, 320, 180);

        using var thumbnail = TileCodec.DecodeTile(jpegBytes);
        var pixel = thumbnail.GetPixel(160, 90);
        Assert.InRange(Math.Abs(pixel.R - 200), 0, 15);
        Assert.InRange(Math.Abs(pixel.G - 30), 0, 15);
        Assert.InRange(Math.Abs(pixel.B - 90), 0, 15);
    }
}
