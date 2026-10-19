using System.Windows.Media;
using System.Windows.Media.Imaging;
using Midview.Master;
using Xunit;

public class GridFilterTests
{
    private static MachineTile Tile(TilePresence presence)
    {
        var tile = new MachineTile(new KnownMachine("LAB", "192.0.2.1", "ANY", null));
        if (presence == TilePresence.Online)
        {
            var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
            image.Freeze();
            tile.ShowThumbnail(image);
        }
        else if (presence == TilePresence.Offline)
        {
            tile.ShowStatus("Offline");
        }
        return tile;
    }

    [Theory]
    [InlineData(TilePresence.Connecting)]
    [InlineData(TilePresence.Online)]
    [InlineData(TilePresence.Offline)]
    public void WithoutTheFilter_EveryMachineShows(TilePresence presence)
    {
        Assert.True(GridFilter.Shows(Tile(presence), onlineOnly: false));
    }

    [Theory]
    [InlineData(TilePresence.Connecting, false)] // not known yet: shows up once it answers
    [InlineData(TilePresence.Online, true)]
    [InlineData(TilePresence.Offline, false)]
    public void OnlineOnly_ShowsTheMachinesThatAnswered(TilePresence presence, bool shown)
    {
        Assert.Equal(shown, GridFilter.Shows(Tile(presence), onlineOnly: true));
    }

    [Fact]
    public void NothingHidden_NeedsNoNotice()
    {
        Assert.Null(GridFilter.EmptyNotice(new[] { Tile(TilePresence.Offline) }, onlineOnly: false));
        Assert.Null(GridFilter.EmptyNotice(new[] { Tile(TilePresence.Offline), Tile(TilePresence.Online) }, onlineOnly: true));
    }

    // The classroom's own hint ("Nenhuma máquina na sala ainda") covers that.
    [Fact]
    public void NoMachinesAtAll_NeedsNoNotice() =>
        Assert.Null(GridFilter.EmptyNotice(Array.Empty<MachineTile>(), onlineOnly: true));

    [Fact]
    public void AllHidden_WhileSomeAreStillConnecting_SaysItIsLooking()
    {
        var notice = GridFilter.EmptyNotice(new[] { Tile(TilePresence.Offline), Tile(TilePresence.Connecting) }, onlineOnly: true);

        Assert.Equal("Procurando as máquinas ligadas...", notice);
    }

    [Fact]
    public void AllHidden_AndNoneOnline_SaysSo()
    {
        var notice = GridFilter.EmptyNotice(new[] { Tile(TilePresence.Offline), Tile(TilePresence.Offline) }, onlineOnly: true);

        Assert.Equal("Nenhuma máquina online agora: todas estão desligadas ou sem resposta.", notice);
    }
}
