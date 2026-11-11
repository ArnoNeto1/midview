using System.Windows.Media;
using System.Windows.Media.Imaging;
using Midview.Master;
using Midview.Protocol;
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

    private static MachineTile PhoneTile(string name = "Cel 01") =>
        new(new KnownMachine(name, "192.0.2.9", "ANY", null, Kind: DeviceKind.Android));

    [Fact]
    public void Phones_filter_hides_computers()
    {
        Assert.False(GridFilter.Shows(Tile(TilePresence.Online), onlineOnly: false, DeviceFilter.Phones));
        Assert.True(GridFilter.Shows(PhoneTile(), onlineOnly: false, DeviceFilter.Phones));
    }

    [Fact]
    public void Computers_filter_hides_phones()
    {
        Assert.True(GridFilter.Shows(Tile(TilePresence.Online), onlineOnly: false, DeviceFilter.Computers));
        Assert.False(GridFilter.Shows(PhoneTile(), onlineOnly: false, DeviceFilter.Computers));
    }

    [Fact]
    public void All_filter_shows_both()
    {
        Assert.True(GridFilter.Shows(Tile(TilePresence.Online), onlineOnly: false, DeviceFilter.All));
        Assert.True(GridFilter.Shows(PhoneTile(), onlineOnly: false, DeviceFilter.All));
    }

    [Fact]
    public void Device_filter_and_online_only_both_apply() =>
        Assert.False(GridFilter.Shows(PhoneTile(), onlineOnly: true, DeviceFilter.Phones)); // a phone not heard from yet is still connecting

    // The big "Preparar celulares" button beside it says the rest.
    [Fact]
    public void Empty_notice_when_filter_leaves_no_phones()
    {
        var onlyComputers = new[] { Tile(TilePresence.Online) };
        Assert.Equal("Nenhum celular nesta sala ainda.",
                     GridFilter.EmptyNotice(onlyComputers, onlineOnly: false, DeviceFilter.Phones));
    }

    [Fact]
    public void Empty_notice_for_phones_shows_even_with_no_machines_at_all()
    {
        Assert.Equal("Nenhum celular nesta sala ainda.",
                     GridFilter.EmptyNotice(Array.Empty<MachineTile>(), onlineOnly: false, DeviceFilter.Phones));
        Assert.Null(GridFilter.EmptyNotice(Array.Empty<MachineTile>(), onlineOnly: false, DeviceFilter.Phones, phonesReady: false));
    }

    [Fact]
    public void Offers_phone_setup_with_no_machines_or_only_computers()
    {
        Assert.True(GridFilter.OffersPhoneSetup(Array.Empty<MachineTile>(), DeviceFilter.Phones, phonesReady: true));
        Assert.True(GridFilter.OffersPhoneSetup(new[] { Tile(TilePresence.Online), Tile(TilePresence.Offline) }, DeviceFilter.Phones, phonesReady: true));
    }

    [Fact]
    public void Does_not_offer_phone_setup_while_the_phone_app_is_not_ready() =>
        Assert.False(GridFilter.OffersPhoneSetup(new[] { Tile(TilePresence.Online) }, DeviceFilter.Phones, phonesReady: false));

    [Fact]
    public void Does_not_offer_phone_setup_with_a_phone_in_the_class()
    {
        Assert.False(GridFilter.OffersPhoneSetup(new[] { Tile(TilePresence.Online), PhoneTile() }, DeviceFilter.Phones, phonesReady: true));
        Assert.False(GridFilter.OffersPhoneSetup(new[] { PhoneTile() }, DeviceFilter.Phones, phonesReady: true));
    }

    [Theory]
    [InlineData(DeviceFilter.All)]
    [InlineData(DeviceFilter.Computers)]
    public void Offers_phone_setup_only_in_the_phones_filter(DeviceFilter devices) =>
        Assert.False(GridFilter.OffersPhoneSetup(new[] { Tile(TilePresence.Online) }, devices, phonesReady: true));

    [Fact]
    public void Empty_notice_for_phones_hides_the_prepare_hint_while_the_phone_app_is_not_ready()
    {
        var onlyComputers = new[] { Tile(TilePresence.Online) };
        Assert.Equal("Nenhum celular nesta sala ainda.",
                     GridFilter.EmptyNotice(onlyComputers, onlineOnly: false, DeviceFilter.Phones, phonesReady: false));
    }

    [Fact]
    public void Empty_notice_when_filter_leaves_no_computers() =>
        Assert.Equal("Nenhum computador nesta sala: só há celulares.",
                     GridFilter.EmptyNotice(new[] { PhoneTile() }, onlineOnly: false, DeviceFilter.Computers));

    [Fact]
    public void No_notice_when_the_device_filter_shows_something() =>
        Assert.Null(GridFilter.EmptyNotice(new[] { Tile(TilePresence.Online), PhoneTile() }, onlineOnly: false, DeviceFilter.Phones));
}
