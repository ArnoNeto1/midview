using Midview.Master;
using Midview.Protocol;
using Xunit;

// The school's picture and PIN for the devices prepared from now on.
public sealed class PhoneDefaultsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "midview-defaults-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };

    [Fact]
    public void Nothing_saved_is_no_defaults()
    {
        Assert.True(PhoneDefaultsFile.Load(_folder).IsEmpty);
    }

    [Fact]
    public void The_pin_and_each_kinds_picture_round_trip_and_are_not_plain_text()
    {
        PhoneDefaultsFile.SavePin("2468", _folder);
        PhoneDefaultsFile.SaveWallpaper(AndroidDeviceKind.Phone, new(PhoneWallpaperScreens.Both, Jpeg), _folder);
        PhoneDefaultsFile.SaveWallpaper(AndroidDeviceKind.Tablet, new(PhoneWallpaperScreens.Lock, new byte[] { 9 }), _folder);

        var defaults = PhoneDefaultsFile.Load(_folder);

        Assert.Equal("2468", defaults.Pin);
        Assert.Equal(PhoneWallpaperScreens.Both, defaults.For(AndroidDeviceKind.Phone)!.Screens);
        Assert.Equal(Jpeg, defaults.For(AndroidDeviceKind.Phone)!.Jpeg);
        Assert.Equal(PhoneWallpaperScreens.Lock, defaults.For(AndroidDeviceKind.Tablet)!.Screens);
        foreach (var file in Directory.GetFiles(_folder))
        {
            Assert.DoesNotContain("2468", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)));
        }
    }

    [Fact]
    public void Taking_both_off_leaves_nothing_on_disk()
    {
        PhoneDefaultsFile.SavePin("1357", _folder);
        PhoneDefaultsFile.SaveWallpaper(AndroidDeviceKind.Phone, new(PhoneWallpaperScreens.Both, Jpeg), _folder);

        PhoneDefaultsFile.SavePin("", _folder);
        Assert.Null(PhoneDefaultsFile.Load(_folder).Pin);
        Assert.NotNull(PhoneDefaultsFile.Load(_folder).Phone);

        PhoneDefaultsFile.SaveWallpaper(AndroidDeviceKind.Phone, null, _folder);
        Assert.True(PhoneDefaultsFile.Load(_folder).IsEmpty);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public void A_pin_that_is_not_4_digits_is_refused()
    {
        Assert.Throws<ArgumentException>(() => PhoneDefaultsFile.SavePin("12a4", _folder));
        Assert.Throws<ArgumentException>(() => PhoneDefaultsFile.SavePin("123", _folder));
    }

    [Fact]
    public void The_kinds_come_from_the_names_and_the_checkbox_says_them()
    {
        Assert.Equal(new[] { AndroidDeviceKind.Phone }, PhoneDefaultsFile.KindsOf(new[] { "Celular 07", "Celular 99" }));
        Assert.Equal(new[] { AndroidDeviceKind.Tablet }, PhoneDefaultsFile.KindsOf(new[] { "Tablet 16" }));
        Assert.Equal(new[] { AndroidDeviceKind.Phone, AndroidDeviceKind.Tablet }, PhoneDefaultsFile.KindsOf(new[] { "Tablet 16", "Celular 07" }));
        Assert.Equal(new[] { AndroidDeviceKind.Phone }, PhoneDefaultsFile.KindsOf(Array.Empty<string>()));

        Assert.Contains("próximos celulares preparados", PhoneDefaultsFile.NextOnes(new[] { AndroidDeviceKind.Phone }));
        Assert.Contains("próximos tablets preparados", PhoneDefaultsFile.NextOnes(new[] { AndroidDeviceKind.Tablet }));
        Assert.Contains("próximos celulares e tablets preparados",
            PhoneDefaultsFile.NextOnes(new[] { AndroidDeviceKind.Phone, AndroidDeviceKind.Tablet }));
    }

    [Fact]
    public void A_prepared_device_gets_its_kinds_picture_then_the_pin()
    {
        var defaults = new PhoneDefaultsFile.Defaults("2468",
            new(PhoneWallpaperScreens.Both, Jpeg), new(PhoneWallpaperScreens.Lock, new byte[] { 9 }));
        var sent = new List<string>();

        var said = PreparedDefaults.Apply("Tablet 07", AndroidDeviceKind.Tablet, defaults,
            wallpaper => { sent.Add("wallpaper " + wallpaper.Screens); return (true, "ok"); },
            pin => { sent.Add("pin " + pin); return (true, "ok"); });

        Assert.Equal(new[] { "wallpaper Lock", "pin 2468" }, sent);
        Assert.Equal("Tablet 07: papel de parede e PIN da escola postos.", said);
    }

    [Fact]
    public void Only_what_is_saved_goes_and_nothing_saved_says_nothing()
    {
        var onlyPin = new PhoneDefaultsFile.Defaults("2468", null, null);
        Assert.Equal("Celular 07: PIN da escola posto.",
            PreparedDefaults.Apply("Celular 07", AndroidDeviceKind.Phone, onlyPin, _ => throw new InvalidOperationException(), _ => (true, "")));

        // A tablet's picture is not a phone's.
        var tabletPicture = new PhoneDefaultsFile.Defaults(null, null, new(PhoneWallpaperScreens.Both, Jpeg));
        Assert.Null(PreparedDefaults.Apply("Celular 07", AndroidDeviceKind.Phone, tabletPicture, _ => (true, ""), _ => (true, "")));
    }

    [Fact]
    public void A_failure_is_said_with_what_did_go()
    {
        var defaults = new PhoneDefaultsFile.Defaults("2468", new(PhoneWallpaperScreens.Both, Jpeg), null);

        var said = PreparedDefaults.Apply("Celular 07", AndroidDeviceKind.Phone, defaults,
            _ => (true, "ok"),
            _ => throw new IOException());

        Assert.Equal("Celular 07: papel de parede da escola posto; não deu para pôr o PIN (o aparelho não respondeu).", said);

        var old = PreparedDefaults.Apply("Celular 07", AndroidDeviceKind.Phone, defaults, _ => null, _ => (false, "o Android recusou"));
        Assert.Equal("Celular 07: não deu para pôr o papel de parede (o Midview do aparelho é antigo); o PIN (o Android recusou).", old);
    }
}
