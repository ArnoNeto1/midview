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
    public void NoFileYet_IsTheDefaultSize() => Assert.Equal(TileSize.Default, GridViewFile.LoadTileWidth(GridViewPath));

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        GridViewFile.SaveTileWidth(GridViewPath, 384);

        Assert.Equal(384, GridViewFile.LoadTileWidth(GridViewPath));
    }

    [Fact]
    public void AnUnreadableFile_IsTheDefaultSize()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(GridViewPath, "{ não é json");

        Assert.Equal(TileSize.Default, GridViewFile.LoadTileWidth(GridViewPath));
    }

    [Fact]
    public void AWidthThatIsNotAStep_IsTheNearestStep()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(GridViewPath, """{ "TileWidth": 300 }""");

        Assert.Equal(320, GridViewFile.LoadTileWidth(GridViewPath));
    }
}
