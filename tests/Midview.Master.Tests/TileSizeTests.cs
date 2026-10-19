using Midview.Master;
using Midview.Protocol;
using Xunit;

public class TileSizeTests
{
    [Fact]
    public void TheDefault_IsTheSizeTilesAlwaysHad()
    {
        Assert.Equal(256, TileSize.Default);
        Assert.Equal(144, TileSize.HeightOf(TileSize.Default));
        Assert.Contains(TileSize.Default, TileSize.Widths);
    }

    [Theory]
    [InlineData(160, 90)]
    [InlineData(320, 180)]
    [InlineData(640, 360)]
    public void HeightOf_Is16By9(int width, int height) => Assert.Equal(height, TileSize.HeightOf(width));

    [Fact]
    public void TheSteps_GoFrom160To640_InOrder()
    {
        Assert.Equal(160, TileSize.Widths[0]);
        Assert.Equal(640, TileSize.Widths[^1]);
        Assert.Equal(TileSize.Widths.Order(), TileSize.Widths);
    }

    [Theory]
    [InlineData(256, 320)]
    [InlineData(160, 192)]
    [InlineData(640, 640)] // already the largest
    [InlineData(300, 320)] // between two steps
    public void Larger_IsTheNextStepUp(int width, int expected) => Assert.Equal(expected, TileSize.Larger(width));

    [Theory]
    [InlineData(256, 224)]
    [InlineData(640, 512)]
    [InlineData(160, 160)] // already the smallest
    [InlineData(300, 256)]
    public void Smaller_IsTheNextStepDown(int width, int expected) => Assert.Equal(expected, TileSize.Smaller(width));

    [Theory]
    [InlineData(256, 256)]
    [InlineData(300, 320)]
    [InlineData(10, 160)]
    [InlineData(5000, 640)]
    public void Snap_IsTheNearestStep(int width, int expected) => Assert.Equal(expected, TileSize.Snap(width));

    // A tile around its thumbnail: border, padding and margin on each side
    // (32 px across), and the name and badges below (60 px), as measured.
    private const double ChromeWidth = 32;
    private const double ChromeHeight = 60;

    [Theory]
    [InlineData(1, 640)]   // one machine: as big as it gets
    [InlineData(4, 512)]   // 2 x 2 at 544 x 348
    [InlineData(12, 256)]  // 4 x 3 at 288 x 204
    [InlineData(30, 160)]  // doesn't fit even at the smallest: the smallest
    public void ToFitAll_IsTheLargestStepWithEveryMachineInView(int machines, int expected)
    {
        Assert.Equal(expected, TileSize.ToFitAll(machines, 1400, 700, ChromeWidth, ChromeHeight));
    }

    [Fact]
    public void ToFitAll_TooNarrowForOneTile_IsTheSmallest()
    {
        Assert.Equal(160, TileSize.ToFitAll(1, 100, 700, ChromeWidth, ChromeHeight));
    }

    [Theory]
    [InlineData(256, 1.0, 256, 144)]
    [InlineData(160, 1.0, 160, 90)]
    [InlineData(256, 1.25, 320, 180)] // Windows at 125%: the tile is that many real pixels
    [InlineData(448, 1.5, 672, 378)]
    [InlineData(640, 1.5, 960, 540)]
    [InlineData(640, 2.0, 960, 540)]  // never past what the Session renders
    public void Request_IsTheTileInScreenPixels_UpToTheMaximum(int width, double dpiScale, int expectedWidth, int expectedHeight)
    {
        Assert.Equal((expectedWidth, expectedHeight), TileSize.Request(width, dpiScale));
    }

    [Fact]
    public void Request_IsAlwaysWithinWhatThePayloadAllows()
    {
        foreach (var width in TileSize.Widths)
        {
            foreach (var dpiScale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0, 3.0 })
            {
                var (requestWidth, requestHeight) = TileSize.Request(width, dpiScale);
                ThumbnailRequest.Encode(requestWidth, requestHeight); // throws when outside
            }
        }
    }
}
