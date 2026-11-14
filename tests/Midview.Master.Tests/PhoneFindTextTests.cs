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

    // Reached at once (a few at a time), answers in the devices' order.
    [Fact]
    public async Task Devices_are_reached_in_parallel_and_answered_in_order()
    {
        var phones = Enumerable.Range(1, 12).Select(i => Phone($"Celular {i:00}", $"T{i}")).ToList();
        var inside = 0;
        var most = 0;
        var gate = new object();

        var answers = await new PhoneFindSender().SendAsync(phones, start: true, phone =>
        {
            lock (gate)
            {
                most = Math.Max(most, ++inside);
            }
            Thread.Sleep(100);
            lock (gate)
            {
                inside--;
            }
            return (true, "tocando por 1 min");
        });

        Assert.NotNull(answers);
        Assert.Equal(phones.Select(p => $"{p.Name} está tocando."), answers!.Select(a => a.Line));
        Assert.True(most > 1, $"one at a time ({most})");
        Assert.True(most <= PhoneFindSender.PhonesAtATime);
    }

    // An unexpected failure is that device's line, never the Master's end.
    [Fact]
    public async Task Any_exception_is_the_devices_own_line()
    {
        var phones = new[] { Phone("Tablet 01", "AA"), Phone("Celular 02", "BB") };

        var answers = await new PhoneFindSender().SendAsync(phones, start: true,
            phone => phone.Name == "Tablet 01" ? throw new ArgumentException("estranho") : (true, "tocando por 1 min"));

        Assert.False(answers![0].Succeeded);
        Assert.StartsWith("Tablet 01: ", answers[0].Line, StringComparison.Ordinal);
        Assert.True(answers[1].Succeeded);
    }

    [Fact]
    public async Task A_second_click_while_running_is_ignored()
    {
        var sender = new PhoneFindSender();
        using var release = new ManualResetEventSlim();
        var phones = new[] { Phone("Tablet 01", "AA") };

        var first = sender.SendAsync(phones, start: true, _ =>
        {
            release.Wait(TimeSpan.FromSeconds(10));
            return (true, "tocando por 1 min");
        });
        Assert.True(sender.Running);
        var calls = 0;
        var second = await sender.SendAsync(phones, start: true, _ =>
        {
            calls++;
            return (true, "");
        });
        release.Set();

        Assert.Null(second);
        Assert.Equal(0, calls);
        Assert.NotNull(await first);
        Assert.False(sender.Running);
        Assert.NotNull(await sender.SendAsync(phones, start: false, _ => (true, "toque parado")));
    }
}
