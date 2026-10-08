using System.Drawing;
using Midview.Protocol;
using Xunit;

public class TileCodecTests
{
    [Fact]
    public void EncodeThenDecode_RoundTripsApproximately()
    {
        using var source = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(source))
        {
            g.Clear(Color.FromArgb(200, 30, 90));
        }

        var encoded = TileCodec.EncodeTile(source, new Rectangle(0, 0, 64, 64));
        using var decoded = TileCodec.DecodeTile(encoded);

        Assert.Equal(64, decoded.Width);
        Assert.Equal(64, decoded.Height);
        var originalPixel = source.GetPixel(32, 32);
        var decodedPixel = decoded.GetPixel(32, 32);
        Assert.InRange(Math.Abs(originalPixel.R - decodedPixel.R), 0, 15);
        Assert.InRange(Math.Abs(originalPixel.G - decodedPixel.G), 0, 15);
        Assert.InRange(Math.Abs(originalPixel.B - decodedPixel.B), 0, 15);
    }

    [Fact]
    public void EncodeTile_SmallerRegionThanBitmap_EncodesOnlyThatRegion()
    {
        using var source = new Bitmap(128, 128);
        using (var g = Graphics.FromImage(source))
        {
            g.Clear(Color.Black);
            g.FillRectangle(Brushes.White, 64, 0, 64, 64);
        }

        var encoded = TileCodec.EncodeTile(source, new Rectangle(64, 0, 64, 64));
        using var decoded = TileCodec.DecodeTile(encoded);

        Assert.Equal(64, decoded.Width);
        Assert.Equal(64, decoded.Height);
    }

    // Part of a 32-bit bitmap, like every screen capture: the JPEG must be
    // that part, from its own corner, not the bitmap's.
    [Fact]
    public void EncodeTile_PartOfA32BitBitmap_IsThatPart()
    {
        using var source = new Bitmap(256, 128, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(source))
        {
            g.Clear(Color.Black);
            g.FillRectangle(Brushes.White, 128, 64, 64, 64);
            g.FillRectangle(Brushes.Red, 192, 64, 64, 64);
        }

        var encoded = TileCodec.EncodeTile(source, new Rectangle(128, 64, 128, 64));
        using var decoded = TileCodec.DecodeTile(encoded);

        Assert.Equal((128, 64), (decoded.Width, decoded.Height));
        var white = decoded.GetPixel(32, 32);
        var red = decoded.GetPixel(96, 32);
        Assert.True(white.R > 225 && white.G > 225 && white.B > 225, $"left half {white}");
        Assert.True(red.R > 200 && red.G < 50 && red.B < 50, $"right half {red}");
    }

    [Fact]
    public void EncodeTile_WholeBitmap_EncodesAllOfIt()
    {
        using var source = new Bitmap(200, 100);
        using (var g = Graphics.FromImage(source))
        {
            g.Clear(Color.Black);
            g.FillRectangle(Brushes.White, 100, 0, 100, 100);
        }

        var encoded = TileCodec.EncodeTile(source, new Rectangle(0, 0, 200, 100));
        using var decoded = TileCodec.DecodeTile(encoded);

        Assert.Equal((200, 100), (decoded.Width, decoded.Height));
        Assert.True(decoded.GetPixel(50, 50).R < 30);
        Assert.True(decoded.GetPixel(150, 50).R > 225);
    }
}
