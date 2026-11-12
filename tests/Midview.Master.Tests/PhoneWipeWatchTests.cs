using Midview.Master;
using Xunit;

public class PhoneWipeWatchTests
{
    private static readonly DateTimeOffset Acked = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_phone_still_answering_90_seconds_after_the_ack_did_not_wipe()
    {
        var watch = new PhoneWipeWatch(Acked);
        Assert.Equal(WipeWatchResult.Waiting, watch.Tick(Acked.AddSeconds(3), online: true));
        Assert.Equal(WipeWatchResult.Waiting, watch.Tick(Acked.AddSeconds(89), online: true));
        Assert.Equal(WipeWatchResult.NotWiped, watch.Tick(Acked.AddSeconds(90), online: true));
    }

    [Fact]
    public void A_phone_that_stops_answering_is_wiping()
    {
        var watch = new PhoneWipeWatch(Acked);
        Assert.Equal(WipeWatchResult.Waiting, watch.Tick(Acked.AddSeconds(3), online: true));
        Assert.Equal(WipeWatchResult.Wiping, watch.Tick(Acked.AddSeconds(20), online: false));
    }

    [Fact]
    public void Once_gone_it_stays_wiping_even_if_it_answers_again_later()
    {
        var watch = new PhoneWipeWatch(Acked);
        watch.Tick(Acked.AddSeconds(10), online: false);
        Assert.Equal(WipeWatchResult.Wiping, watch.Tick(Acked.AddMinutes(5), online: true));
    }

    [Fact]
    public void The_wait_is_90_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(90), PhoneWipeWatch.NotWipedAfter);
    }
}
