using Midview.Master;
using Xunit;

public class TileCellsFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"midview-cells-{Guid.NewGuid():N}");

    private string CellsPath => Path.Combine(_directory, "tile-cells.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void NoFileYet_IsNoPlaces() => Assert.Empty(TileCellsFile.Load(CellsPath));

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        TileCellsFile.Save(CellsPath, new Dictionary<string, TileCell> { ["LAB-01"] = new(0, 0), ["LAB-02"] = new(2.5, 1) });

        var cells = TileCellsFile.Load(CellsPath);

        Assert.Equal(new TileCell(0, 0), cells["LAB-01"]);
        Assert.Equal(new TileCell(2.5, 1), cells["LAB-02"]);
    }

    [Fact]
    public void AMachine_IsFoundWhateverTheCaseOfItsName()
    {
        TileCellsFile.Save(CellsPath, new Dictionary<string, TileCell> { ["LAB-01"] = new(3, 1) });

        Assert.Equal(new TileCell(3, 1), TileCellsFile.Load(CellsPath)["lab-01"]);
    }

    [Fact]
    public void AnUnreadableFile_IsNoPlaces()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(CellsPath, "{ não é json");

        Assert.Empty(TileCellsFile.Load(CellsPath));
    }
}
