using System.Drawing;
using TreezeSuite.Protocol;
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
}
