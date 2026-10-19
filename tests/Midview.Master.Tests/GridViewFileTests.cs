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

    [Fact]
    public void AWidthThatIsNotAStep_IsTheNearestStep()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(GridViewPath, """{ "TileWidth": 300 }""");

        Assert.Equal(320, GridViewFile.Load(GridViewPath).TileWidth);
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
