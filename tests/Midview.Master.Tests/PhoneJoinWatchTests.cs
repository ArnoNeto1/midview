using Midview.Master;
using Xunit;

public class PhoneJoinWatchTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private static readonly Func<string, bool> NoneOnline = _ => false;

    [Fact]
    public void Nothing_watched_asks_for_no_search()
    {
        var watch = new PhoneJoinWatch();

        Assert.False(watch.Tick(T0, NoneOnline));
        Assert.True(watch.IsEmpty);
    }

    [Fact]
    public void A_phone_watched_asks_for_a_search_and_then_every_10_seconds()
    {
        var watch = new PhoneJoinWatch();
        watch.Watch("AA", T0);

        Assert.True(watch.Tick(T0, NoneOnline));
        watch.Searched(T0);
        Assert.False(watch.Tick(T0 + TimeSpan.FromSeconds(9), NoneOnline));
        Assert.True(watch.Tick(T0 + TimeSpan.FromSeconds(10), NoneOnline));
        Assert.True(watch.IsWatching("AA"));
    }

    [Fact]
    public void A_phone_online_stops_being_watched()
    {
        var watch = new PhoneJoinWatch();
        watch.Watch("AA", T0);

        Assert.False(watch.Tick(T0 + TimeSpan.FromSeconds(20), thumbprint => thumbprint == "AA"));

        Assert.False(watch.IsWatching("AA"));
        Assert.True(watch.IsEmpty);
    }

    [Fact]
    public void A_phone_not_online_after_5_minutes_stops_being_watched()
    {
        var watch = new PhoneJoinWatch();
        watch.Watch("AA", T0);

        Assert.True(watch.Tick(T0 + TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59), NoneOnline));
        Assert.False(watch.Tick(T0 + TimeSpan.FromMinutes(5), NoneOnline));

        Assert.True(watch.IsEmpty);
    }

    [Fact]
    public void Several_phones_share_one_search_every_10_seconds()
    {
        var watch = new PhoneJoinWatch();
        watch.Watch("AA", T0);
        watch.Watch("BB", T0 + TimeSpan.FromSeconds(3));

        Assert.True(watch.Tick(T0 + TimeSpan.FromSeconds(3), NoneOnline));
        watch.Searched(T0 + TimeSpan.FromSeconds(3));

        Assert.False(watch.Tick(T0 + TimeSpan.FromSeconds(8), NoneOnline));
        Assert.True(watch.IsWatching("AA"));
        Assert.True(watch.IsWatching("BB"));
    }

    [Fact]
    public void Each_phone_has_its_own_5_minutes()
    {
        var watch = new PhoneJoinWatch();
        watch.Watch("AA", T0);
        watch.Watch("BB", T0 + TimeSpan.FromMinutes(2));

        watch.Tick(T0 + TimeSpan.FromMinutes(5), NoneOnline);

        Assert.False(watch.IsWatching("AA"));
        Assert.True(watch.IsWatching("BB"));
    }

    [Fact]
    public void Watching_again_restarts_the_5_minutes_and_searches_at_once()
    {
        var watch = new PhoneJoinWatch();
        watch.Watch("AA", T0);
        watch.Searched(T0 + TimeSpan.FromMinutes(4));
        watch.Tick(T0 + TimeSpan.FromMinutes(6), NoneOnline);
        Assert.False(watch.IsWatching("AA"));

        watch.Watch("AA", T0 + TimeSpan.FromMinutes(7));

        Assert.True(watch.Tick(T0 + TimeSpan.FromMinutes(7), NoneOnline));
        Assert.True(watch.Tick(T0 + TimeSpan.FromMinutes(11), NoneOnline));
    }

    [Fact]
    public void Thumbprints_match_whatever_the_case()
    {
        var watch = new PhoneJoinWatch();
        watch.Watch("ab12", T0);

        Assert.True(watch.IsWatching("AB12"));
    }
}
