using Midview.Master;
using Midview.Protocol;
using Xunit;

public class TileSizeTests
{
    // A tile around its thumbnail: border, padding and margin on each side
    // (32 px across), and the name and badges below (60 px).
    private const double ChromeWidth = 32;
    private const double ChromeHeight = 60;

    [Fact]
    public void TheDefault_IsTheSizeTilesAlwaysHad()
    {
        Assert.Equal(256, TileSize.Default);
        Assert.Equal(144, TileSize.HeightOf(TileSize.Default));
        Assert.InRange(TileSize.Default, TileSize.Smallest, TileSize.Largest);
    }

    [Theory]
    [InlineData(160, 90)]
    [InlineData(320, 180)]
    [InlineData(640, 360)]
    public void HeightOf_Is16By9(int width, int height) => Assert.Equal(height, TileSize.HeightOf(width));

    [Theory]
    [InlineData(100, 120)]
    [InlineData(300, 300)] // anything in between, no steps
    [InlineData(5000, 960)]
    public void Clamp_KeepsTheSizeInRange(int width, int expected) => Assert.Equal(expected, TileSize.Clamp(width));

    // A row 1400 wide.
    [Theory]
    [InlineData(256, 318)] // 4 across: 1400 / 4 - 32
    [InlineData(320, 434)] // 3 across
    [InlineData(384, 434)] // 3 across too: drawn the same as 320
    [InlineData(640, 668)] // 2 across
    public void ToFillRow_WidensTheTilesToShareTheRow(int width, int expected) =>
        Assert.Equal(expected, TileSize.ToFillRow(width, 1400, ChromeWidth));

    [Fact]
    public void ToFillRow_NoRowYetOrTooNarrowForOne_IsTheWidth()
    {
        Assert.Equal(256, TileSize.ToFillRow(256, 0, ChromeWidth));
        Assert.Equal(640, TileSize.ToFillRow(640, 500, ChromeWidth));
    }

    [Theory]
    [InlineData(256, 434)] // 4 across, then 3
    [InlineData(434, 668)] // 3, then 2
    [InlineData(668, 960)] // 2, then 1: as big as it gets
    [InlineData(960, 960)]
    public void Larger_InARow_IsOneFewerAcross(int width, int expected) =>
        Assert.Equal(expected, TileSize.Larger(width, 1400, ChromeWidth));

    [Theory]
    [InlineData(256, 248)] // 4 across, then 5
    [InlineData(130, 123)] // 8, then 9
    [InlineData(123, 120)] // 10 across would be under the smallest
    public void Smaller_InARow_IsOneMoreAcross(int width, int expected) =>
        Assert.Equal(expected, TileSize.Smaller(width, 1400, ChromeWidth));

    [Fact]
    public void LargerAndSmaller_PlacedByHand_AreAnEighth()
    {
        Assert.Equal(288, TileSize.Larger(256, 0, ChromeWidth));
        Assert.Equal(228, TileSize.Smaller(256, 0, ChromeWidth));
        Assert.Equal(960, TileSize.Larger(950, 0, ChromeWidth));
        Assert.Equal(120, TileSize.Smaller(125, 0, ChromeWidth));
    }

    [Theory]
    [InlineData(1, 668)]   // one alone is too tall for the window: 2 across
    [InlineData(4, 434)]   // 3 + 1 at 466 x 304: two across, at 668, are too tall
    [InlineData(12, 248)]  // 5 + 5 + 2 at 280 x 199
    [InlineData(30, 143)]  // 8 across, 4 rows
    [InlineData(100, 120)] // doesn't fit even at the smallest: the smallest
    public void ToFitAll_IsTheLargestWidthWithEveryMachineInView(int machines, int expected) =>
        Assert.Equal(expected, TileSize.ToFitAll(machines, 1400, 700, ChromeWidth, ChromeHeight));

    [Fact]
    public void ToFitAll_TooNarrowForOneTile_IsTheSmallest() =>
        Assert.Equal(120, TileSize.ToFitAll(1, 100, 700, ChromeWidth, ChromeHeight));

    [Theory]
    [InlineData(5, 3, 248)] // as wide as five fit across
    [InlineData(2, 2, 515)] // as tall as two fit down
    [InlineData(1, 1, 960)] // no bigger than the largest
    public void ToFitRoom_IsTheLargestWidthWithTheWholeRoomInView(double columns, double rows, int expected) =>
        Assert.Equal(expected, TileSize.ToFitRoom(columns, rows, 1400, 700, ChromeWidth, ChromeHeight));

    [Theory]
    [InlineData(256, 1.0, 256, 144)]
    [InlineData(160, 1.0, 160, 90)]
    [InlineData(256, 1.25, 320, 180)] // Windows at 125%: the tile is that many real pixels
    [InlineData(448, 1.5, 672, 378)]
    [InlineData(640, 1.5, 960, 540)]
    [InlineData(640, 2.0, 960, 540)]  // never past what the Session renders
    public void Request_IsTheTileInScreenPixels_UpToTheMaximum(int width, double dpiScale, int expectedWidth, int expectedHeight) =>
        Assert.Equal((expectedWidth, expectedHeight), TileSize.Request(width, dpiScale));

    [Fact]
    public void Request_IsAlwaysWithinWhatThePayloadAllows()
    {
        for (var width = TileSize.Smallest; width <= TileSize.Largest * 2; width += 8)
        {
            foreach (var dpiScale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0, 3.0 })
            {
                var (requestWidth, requestHeight) = TileSize.Request(width, dpiScale);
                ThumbnailRequest.Encode(requestWidth, requestHeight); // throws when outside
            }
        }
    }
}
