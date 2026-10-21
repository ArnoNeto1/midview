using Midview.Master;
using Xunit;

public class GridViewFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"midview-grid-{Guid.NewGuid():N}");

    private string GridViewPath => Path.Combine(_directory, "grid-view.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void NoFileYet_IsTheDefaultSize_WithEveryMachineShown() =>
        Assert.Equal(new GridView(TileSize.Default, OnlineOnly: false), GridViewFile.Load(GridViewPath));

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        GridViewFile.Save(GridViewPath, new GridView(384, OnlineOnly: true));

        Assert.Equal(new GridView(384, OnlineOnly: true), GridViewFile.Load(GridViewPath));
    }

    [Fact]
    public void AnUnreadableFile_IsTheDefault()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(GridViewPath, "{ não é json");

        Assert.Equal(new GridView(TileSize.Default, OnlineOnly: false), GridViewFile.Load(GridViewPath));
    }

    [Theory]
    [InlineData(300, 300)] // any size in range, no steps
    [InlineData(5000, 960)]
    [InlineData(10, 120)]
    public void AWidthOutOfRange_IsBroughtIntoIt(int saved, int expected)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(GridViewPath, $$"""{ "TileWidth": {{saved}} }""");

        Assert.Equal(expected, GridViewFile.Load(GridViewPath).TileWidth);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsTheLayout()
    {
        var view = new GridView(300, OnlineOnly: false, FitAll: true, Arranged: true);
        GridViewFile.Save(GridViewPath, view);

        Assert.Equal(view, GridViewFile.Load(GridViewPath));
    }

    // What 0.0.22 saved, before "Visualizar todos" stayed on and the room.
    [Fact]
    public void AFileWithOnlyTheSizeAndFilter_HasTheRestOff()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(GridViewPath, """{"TileWidth":256,"OnlineOnly":true}""");

        Assert.Equal(new GridView(256, OnlineOnly: true, FitAll: false, Arranged: false),
            GridViewFile.Load(GridViewPath));
    }

    // What 0.0.19 saved, before the filter existed.
    [Fact]
    public void AFileWithOnlyTheSize_ShowsEveryMachine()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(GridViewPath, """{"TileWidth":384}""");

        Assert.Equal(new GridView(384, OnlineOnly: false), GridViewFile.Load(GridViewPath));
    }

    [Fact]
    public void NoSize_ButTheFilter_IsTheDefaultSizeWithTheFilter()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(GridViewPath, """{ "OnlineOnly": true }""");

        Assert.Equal(new GridView(TileSize.Default, OnlineOnly: true), GridViewFile.Load(GridViewPath));
    }
}
