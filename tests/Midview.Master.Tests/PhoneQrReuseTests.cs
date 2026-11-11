using Midview.Master;
using Xunit;

public class PhoneQrReuseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 14, 0, 0, TimeSpan.Zero);

    private static ShownQr Shown(string number = "07", string ssid = "Lab", string password = "senha-do-lab", TimeSpan? left = null) =>
        new(number, ssid, password, "token", Now + (left ?? TimeSpan.FromMinutes(50)));

    [Fact]
    public void Same_number_and_wifi_with_time_left_is_shown_again() =>
        Assert.True(PhoneQrReuse.Reuse(Shown(), "07", "Lab", "senha-do-lab", Now));

    [Fact]
    public void Nothing_shown_yet_needs_a_new_one() =>
        Assert.False(PhoneQrReuse.Reuse(null, "07", "Lab", "senha-do-lab", Now));

    [Fact]
    public void Another_number_needs_a_new_one() =>
        Assert.False(PhoneQrReuse.Reuse(Shown(), "08", "Lab", "senha-do-lab", Now));

    [Fact]
    public void Under_10_minutes_left_needs_a_new_one()
    {
        Assert.False(PhoneQrReuse.Reuse(Shown(left: TimeSpan.FromMinutes(9)), "07", "Lab", "senha-do-lab", Now));
        Assert.False(PhoneQrReuse.Reuse(Shown(left: TimeSpan.FromMinutes(10)), "07", "Lab", "senha-do-lab", Now));
        Assert.True(PhoneQrReuse.Reuse(Shown(left: TimeSpan.FromMinutes(11)), "07", "Lab", "senha-do-lab", Now));
    }

    [Fact]
    public void A_changed_wifi_needs_a_new_one()
    {
        Assert.False(PhoneQrReuse.Reuse(Shown(), "07", "Lab-2", "senha-do-lab", Now));
        Assert.False(PhoneQrReuse.Reuse(Shown(), "07", "Lab", "outra-senha", Now));
    }
}
