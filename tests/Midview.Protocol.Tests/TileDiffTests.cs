using System.Drawing;
using Midview.Protocol;
using Xunit;

public class TileDiffTests
{
    [Fact]
    public void FindChangedTiles_IdenticalBitmaps_ReturnsEmpty()
    {
        using var a = new Bitmap(128, 128);
        using var b = new Bitmap(128, 128);
        FillSolid(a, Color.Red);
        FillSolid(b, Color.Red);

        var changed = TileDiff.FindChangedTiles(a, b);

        Assert.Empty(changed);
    }

    [Fact]
    public void FindChangedTiles_OneTileDifferent_ReturnsOnlyThatTile()
    {
        using var a = new Bitmap(128, 128);
        using var b = new Bitmap(128, 128);
        FillSolid(a, Color.Red);
        FillSolid(b, Color.Red);
        for (var y = 0; y < 64; y++)
        {
            for (var x = 64; x < 128; x++)
            {
                b.SetPixel(x, y, Color.Blue);
            }
        }

        var changed = TileDiff.FindChangedTiles(a, b);

        Assert.Equal(new[] { new TileCoord(0, 1) }, changed);
    }

    [Fact]
    public void FindChangedTiles_DifferentSizes_Throws()
    {
        using var a = new Bitmap(128, 128);
        using var b = new Bitmap(64, 64);

        Assert.Throws<ArgumentException>(() => TileDiff.FindChangedTiles(a, b));
    }

    private static void FillSolid(Bitmap bitmap, Color color)
    {
        using var g = Graphics.FromImage(bitmap);
        g.Clear(color);
    }
}
