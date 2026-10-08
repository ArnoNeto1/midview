using System.Drawing;
using Midview.Protocol;
using Xunit;

public class TileRegionsTests
{
    [Fact]
    public void Merge_NoTiles_IsEmpty()
    {
        Assert.Empty(TileRegions.Merge(Array.Empty<TileCoord>(), 1920, 1080));
    }

    [Fact]
    public void Merge_OneTile_IsThatTile()
    {
        var regions = TileRegions.Merge(new[] { new TileCoord(2, 3) }, 1920, 1080);

        Assert.Equal(new[] { new Rectangle(192, 128, 64, 64) }, regions);
    }

    [Fact]
    public void Merge_TileOnTheEdge_StopsAtTheImage()
    {
        // 1080 = 16 * 64 + 56: the last row of tiles is 56 px tall.
        var regions = TileRegions.Merge(new[] { new TileCoord(16, 29) }, 1900, 1080);

        Assert.Equal(new[] { new Rectangle(1856, 1024, 44, 56) }, regions);
    }

    [Fact]
    public void Merge_EveryTile_IsTheWholeImage()
    {
        var regions = TileRegions.Merge(TileDiff.AllTiles(1920, 1080), 1920, 1080);

        Assert.Equal(new[] { new Rectangle(0, 0, 1920, 1080) }, regions);
    }

    [Fact]
    public void Merge_MostOfAnArea_IsThatWholeArea()
    {
        // 3 of the 4 tiles of a 2x2 block.
        var tiles = new[] { new TileCoord(1, 1), new TileCoord(1, 2), new TileCoord(2, 1) };

        var regions = TileRegions.Merge(tiles, 1920, 1080);

        Assert.Equal(new[] { new Rectangle(64, 64, 128, 128) }, regions);
    }

    [Fact]
    public void Merge_FarApartTiles_StaySeparate()
    {
        var tiles = new[] { new TileCoord(0, 0), new TileCoord(10, 20) };

        var regions = TileRegions.Merge(tiles, 1920, 1080);

        Assert.Equal(new[] { new Rectangle(0, 0, 64, 64), new Rectangle(1280, 640, 64, 64) }, regions);
    }

    [Fact]
    public void Merge_SideBySideTiles_BecomeOneRow_AndLinedUpRowsOneRectangle()
    {
        // A 3-wide, 2-tall block at the top left, and a lone tile far away
        // (so the area around everything is mostly unchanged).
        var tiles = new[]
        {
            new TileCoord(0, 0), new TileCoord(0, 1), new TileCoord(0, 2),
            new TileCoord(1, 0), new TileCoord(1, 1), new TileCoord(1, 2),
            new TileCoord(15, 25),
        };

        var regions = TileRegions.Merge(tiles, 1920, 1080);

        Assert.Equal(new[] { new Rectangle(0, 0, 192, 128), new Rectangle(1600, 960, 64, 64) }, regions);
    }

    [Fact]
    public void Merge_RowsThatDoNotLineUp_StaySeparateRectangles()
    {
        // A staircase: a third of the area around it changed.
        var tiles = new[]
        {
            new TileCoord(0, 0), new TileCoord(0, 1),
            new TileCoord(1, 2), new TileCoord(1, 3),
            new TileCoord(2, 4), new TileCoord(2, 5),
        };

        var regions = TileRegions.Merge(tiles, 1920, 1080);

        Assert.Equal(
            new[] { new Rectangle(0, 0, 128, 64), new Rectangle(128, 64, 128, 64), new Rectangle(256, 128, 128, 64) },
            regions);
    }

    // Within a group that fills little of its area, lined-up runs still make
    // one taller rectangle.
    [Fact]
    public void Merge_LinedUpRowsInASparseGroup_BecomeOneRectangle()
    {
        var tiles = new[]
        {
            new TileCoord(0, 0), new TileCoord(0, 1),
            new TileCoord(1, 0), new TileCoord(1, 1),
            new TileCoord(2, 3), new TileCoord(4, 5), new TileCoord(6, 7),
        };

        var regions = TileRegions.Merge(tiles, 1920, 1080);

        Assert.Equal(
            new[]
            {
                new Rectangle(0, 0, 128, 128), new Rectangle(192, 128, 64, 64),
                new Rectangle(320, 256, 64, 64), new Rectangle(448, 384, 64, 64),
            },
            regions);
    }

    // A video playing at the top left while the mouse moves at the bottom
    // right: two JPEGs, not one of everything in between.
    [Fact]
    public void Merge_ChangesFarApart_NeverShareAJpeg_EvenWhenTogetherTheyFillHalfTheArea()
    {
        var tiles = new List<TileCoord>();
        for (var row = 0; row < 12; row++)
        {
            for (var column = 0; column < 20; column++)
            {
                tiles.Add(new TileCoord(row, column));
            }
        }
        tiles.Add(new TileCoord(15, 28));

        var regions = TileRegions.Merge(tiles, 1920, 1080);

        Assert.Equal(new[] { new Rectangle(0, 0, 1280, 768), new Rectangle(1792, 960, 64, 64) }, regions);
    }

    [Fact]
    public void Merge_ChangesOneTileApart_AreOneGroup()
    {
        var tiles = new[] { new TileCoord(0, 0), new TileCoord(0, 2) };

        var regions = TileRegions.Merge(tiles, 1920, 1080);

        Assert.Equal(new[] { new Rectangle(0, 0, 192, 64) }, regions);
    }

    // A whole 4K screen: in bands, so no JPEG gets too big for one message.
    [Fact]
    public void Merge_AHugeArea_GoesInBands()
    {
        var regions = TileRegions.Merge(TileDiff.AllTiles(3840, 2160), 3840, 2160);

        Assert.Equal(new[] { new Rectangle(0, 0, 3840, 1088), new Rectangle(0, 1088, 3840, 1072) }, regions);
    }

    [Fact]
    public void Merge_TheSameTileTwice_CountsOnce()
    {
        var regions = TileRegions.Merge(new[] { new TileCoord(1, 1), new TileCoord(1, 1) }, 1920, 1080);

        Assert.Equal(new[] { new Rectangle(64, 64, 64, 64) }, regions);
    }

    [Fact]
    public void Merge_ScatteredTiles_CoverEveryChangedTile_AndNothingOutsideTheImage()
    {
        var tiles = new List<TileCoord>();
        for (var row = 0; row < 17; row++)
        {
            for (var column = 0; column < 30; column++)
            {
                if ((row * 7 + column * 3) % 5 == 0)
                {
                    tiles.Add(new TileCoord(row, column));
                }
            }
        }

        var regions = TileRegions.Merge(tiles, 1910, 1075);

        foreach (var tile in tiles)
        {
            var rect = TileDiff.TileRect(tile.Row, tile.Column, 1910, 1075);
            Assert.Contains(regions, region => region.Contains(rect));
        }
        Assert.All(regions, region => Assert.True(new Rectangle(0, 0, 1910, 1075).Contains(region)));
    }
}
