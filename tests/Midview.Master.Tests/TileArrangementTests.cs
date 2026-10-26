using Midview.Master;
using Xunit;

public class TileArrangementTests
{
    [Theory]
    [InlineData(0, 5, 0, 0)]
    [InlineData(7, 5, 2, 1)]
    [InlineData(3, 0, 0, 3)] // no row width yet: one across
    public void FromRows_IsWhereTheRowsShowedTheTile(int index, int columns, double x, double y) =>
        Assert.Equal(new TileCell(x, y), TileArrangement.FromRows(index, columns));

    [Fact]
    public void FirstFree_InAnEmptyRoom_IsTheCorner() =>
        Assert.Equal(new TileCell(0, 0), TileArrangement.FirstFree([], 5));

    [Fact]
    public void FirstFree_IsTheNextCellAlongTheRow() =>
        Assert.Equal(new TileCell(2, 0), TileArrangement.FirstFree([new(0, 0), new(1, 0)], 5));

    [Fact]
    public void FirstFree_ARoomWiderThanTheRows_KeepsItsWidth() =>
        Assert.Equal(new TileCell(0, 1), TileArrangement.FirstFree([new(0, 0), new(1, 0), new(2, 0)], 2));

    [Fact]
    public void FirstFree_ATileOffItsCell_StillTakesIt() =>
        Assert.Equal(new TileCell(1, 0), TileArrangement.FirstFree([new(0.4, 0.1)], 5));

    [Fact]
    public void Dropped_OffTheTopOrLeft_IsBackAtTheEdge()
    {
        Assert.Equal(new TileCell(0, 2.2), TileArrangement.Dropped(new TileCell(-0.5, 2.2)));
        Assert.Equal(new TileCell(1.5, 0), TileArrangement.Dropped(new TileCell(1.5, -3)));
    }

    [Fact]
    public void Aligned_PutsEachTileOnTheNearestWholeCell() =>
        Assert.Equal(
            [new TileCell(0, 0), new TileCell(2, 0), new TileCell(1, 3)],
            TileArrangement.Aligned([new(0.2, 0.1), new(1.6, 0.4), new(0.9, 2.8)]));

    [Fact]
    public void Aligned_TwoOnOneCell_TheLaterTakesTheNearestFree() =>
        Assert.Equal(
            [new TileCell(0, 0), new TileCell(1, 0)],
            TileArrangement.Aligned([new(0.1, 0), new(0.2, 0)]));

    [Fact]
    public void Aligned_ATileAlreadyOnACell_StaysThere_TheDraggedOneMoves() =>
        Assert.Equal(
            [new TileCell(1, 0), new TileCell(0, 0), new TileCell(2, 0)],
            TileArrangement.Aligned([new(0.1, 0.1), new(0, 0), new(2, 0)]));

    [Fact]
    public void Aligned_NeverOffTheTopOrLeft() =>
        Assert.Equal(
            [new TileCell(0, 0), new TileCell(0, 1)], // below is nearer to -0.3 than the right is
            TileArrangement.Aligned([new(0, 0), new(-0.3, 0)]));

    [Fact]
    public void Extent_IsTheRoomFromItsCorner()
    {
        Assert.Equal((1.0, 1.0), TileArrangement.Extent([]));
        Assert.Equal((5.0, 3.0), TileArrangement.Extent([new(0, 0), new(4, 2)]));
        Assert.Equal((3.5, 1.0), TileArrangement.Extent([new(2.5, 0)]));
    }

    // A machine renamed in the room keeps its place, and its old name none.
    [Fact]
    public void Renamed_KeepsThePlaceUnderTheNewName()
    {
        var cells = new Dictionary<string, TileCell>(StringComparer.OrdinalIgnoreCase)
        {
            ["Aluno-23"] = new(3, 1),
            ["Aluno-24"] = new(4, 1),
        };

        TileArrangement.Renamed(cells, "Aluno-23", "Sala2-PC05");

        Assert.Equal(new TileCell(3, 1), cells["Sala2-PC05"]);
        Assert.False(cells.ContainsKey("Aluno-23"));
        Assert.Equal(new TileCell(4, 1), cells["Aluno-24"]);
    }

    [Fact]
    public void Renamed_WithNoPlaceYet_ChangesNothing()
    {
        var cells = new Dictionary<string, TileCell>(StringComparer.OrdinalIgnoreCase) { ["Aluno-24"] = new(4, 1) };

        TileArrangement.Renamed(cells, "Aluno-23", "Sala2-PC05");

        Assert.Equal(["Aluno-24"], cells.Keys);
    }

    // Only the case changed: the same name, the same place.
    [Fact]
    public void Renamed_OnlyInCase_KeepsThePlace()
    {
        var cells = new Dictionary<string, TileCell>(StringComparer.OrdinalIgnoreCase) { ["aluno-23"] = new(3, 1) };

        TileArrangement.Renamed(cells, "aluno-23", "ALUNO-23");

        Assert.Equal(new TileCell(3, 1), cells["ALUNO-23"]);
        Assert.Single(cells);
    }
}
