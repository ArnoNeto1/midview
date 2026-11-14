using Midview.Master;
using Midview.Protocol;
using Xunit;

namespace Midview.Master.Tests;

// "Tocar para encontrar": the menus' words, one status line per device and
// which devices are ringing.
public sealed class PhoneFindTextTests
{
    private static KnownMachine Phone(string name, string thumb) => new(name, "10.0.0.5", thumb, thumb, null, DeviceKind.Android);

    [Fact]
    public void Menu_says_parar_only_while_ringing()
    {
        Assert.Equal("Tocar para encontrar", PhoneFindText.MenuItem(allRinging: false));
        Assert.Equal("Parar o toque", PhoneFindText.MenuItem(allRinging: true));
    }

    [Theory]
    [InlineData(true, true, "tocando por 1 min", "Tablet 01 está tocando.")]
    [InlineData(false, true, "toque parado", "Tablet 01 parou de tocar.")]
    [InlineData(false, true, "não estava tocando", "Tablet 01 parou de tocar.")]
    [InlineData(true, false, "não foi possível tocar nenhum som", "Tablet 01: não foi possível tocar nenhum som")]
    [InlineData(true, false, "Ainda não funciona em celular.", "Tablet 01: o Midview deste aparelho é de uma versão anterior; atualize-o para tocar.")]
    public void One_line_per_device(bool start, bool succeeded, string detail, string line)
    {
        Assert.Equal(line, PhoneFindText.Summary("Tablet 01", start, succeeded, detail));
    }

    [Fact]
    public void Ringing_lasts_the_time_asked_or_until_stopped()
    {
        var tablet = Phone("Tablet 01", "AA");
        var phone = Phone("Celular 02", "BB");
        var ringing = new PhoneFindRinging();
        var now = new DateTime(2026, 11, 13, 10, 0, 0);

        Assert.False(ringing.AllRinging(new[] { tablet }, now));
        ringing.Started(new[] { tablet, phone }, now, PhoneFind.DefaultSeconds);
        Assert.True(ringing.AllRinging(new[] { tablet, phone }, now.AddSeconds(59)));
        Assert.False(ringing.IsRinging(tablet, now.AddSeconds(60)));

        ringing.Stopped(new[] { phone });
        Assert.False(ringing.AllRinging(new[] { tablet, phone }, now.AddSeconds(10)));
        Assert.True(ringing.AllRinging(new[] { tablet }, now.AddSeconds(10)));
        Assert.False(ringing.AllRinging(Array.Empty<KnownMachine>(), now));
    }
}
