using System.Drawing;
using Midview.Protocol;
using Midview.Session;
using Xunit;

public class ThumbnailRendererTests
{
    [Theory]
    [InlineData(1920, 1080, 320, 180)] // 16:9
    [InlineData(1280, 800, 288, 180)]  // 16:10
    [InlineData(1024, 768, 240, 180)]  // 4:3
    [InlineData(1280, 1024, 225, 180)] // 5:4
    [InlineData(3840, 1080, 320, 90)]  // very wide
    [InlineData(200, 100, 200, 100)]   // already smaller: never scaled up
    public void FitWithin_KeepsAspectRatioInsideTheBox(int width, int height, int expectedWidth, int expectedHeight)
    {
        var size = ThumbnailRenderer.FitWithin(width, height, ThumbnailRenderer.MaxWidth, ThumbnailRenderer.MaxHeight);

        Assert.Equal(new Size(expectedWidth, expectedHeight), size);
    }

    [Fact]
    public void Render_ReturnsAJpegOfTheFittedSize()
    {
        var jpeg = ThumbnailRenderer.Render(new FixedSizeScreenCapture(1024, 768));

        using var thumbnail = TileCodec.DecodeTile(jpeg);
        Assert.Equal(240, thumbnail.Width);
        Assert.Equal(180, thumbnail.Height);
    }

    [Fact]
    public void Render_WithinALargerBox_ReturnsALargerJpeg()
    {
        var jpeg = ThumbnailRenderer.Render(new FixedSizeScreenCapture(1920, 1080), 640, 360);

        using var thumbnail = TileCodec.DecodeTile(jpeg);
        Assert.Equal(640, thumbnail.Width);
        Assert.Equal(360, thumbnail.Height);
    }
}
