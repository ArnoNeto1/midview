using Midview.Master;
using Midview.Protocol;
using Xunit;

public class PhoneClaimWatcherTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Agent = new('A', 40);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "midview-watch-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static FoundMachine Phone(string? claim) =>
        new("Celular-x", "10.0.0.7", Agent, TeacherStatus.None, true, DeviceKind.Android, claim);

    private static KnownMachine Joined() => new("Celular 07", "10.0.0.7", new string('5', 40), Agent, null, DeviceKind.Android);

    [Fact]
    public void Without_pending_claims_it_does_not_search()
    {
        var searches = 0;
        using var watcher = new PhoneClaimWatcher(
            () => { searches++; return Array.Empty<FoundMachine>(); }, (_, _) => Joined(), _ => { }, () => Now, _folder, timed: false);

        Assert.False(watcher.CheckOnce());
        Assert.Equal(0, searches);
    }

    [Fact]
    public void Matching_claim_id_claims_once_adds_and_forgets_the_pending()
    {
        var pending = new PendingPhoneClaim("07", "tok-a", Now.AddHours(1));
        PendingPhoneClaimsFile.Add(pending, _folder);
        var claims = new List<PendingPhoneClaim>();
        var added = new List<KnownMachine>();
        using var watcher = new PhoneClaimWatcher(
            () => new[] { Phone(PhoneClaim.ClaimId("tok-a")) }, (_, p) => { claims.Add(p); return Joined(); }, added.Add, () => Now, _folder, timed: false);

        Assert.True(watcher.CheckOnce());

        Assert.Equal(new[] { pending }, claims);
        Assert.Equal(new[] { Joined() }, added);
        Assert.Empty(PendingPhoneClaimsFile.LoadValid(Now, _folder));
    }

    [Fact]
    public void Another_claim_id_does_nothing()
    {
        PendingPhoneClaimsFile.Add(new PendingPhoneClaim("07", "tok-a", Now.AddHours(1)), _folder);
        var claims = 0;
        using var watcher = new PhoneClaimWatcher(
            () => new[] { Phone(PhoneClaim.ClaimId("other")), Phone(null) }, (_, _) => { claims++; return Joined(); }, _ => { }, () => Now, _folder, timed: false);

        Assert.True(watcher.CheckOnce());

        Assert.Equal(0, claims);
        Assert.Single(PendingPhoneClaimsFile.LoadValid(Now, _folder));
    }

    [Fact]
    public void A_failing_claim_leaves_the_pending_for_the_next_round()
    {
        PendingPhoneClaimsFile.Add(new PendingPhoneClaim("07", "tok-a", Now.AddHours(1)), _folder);
        var added = 0;
        using var watcher = new PhoneClaimWatcher(
            () => new[] { Phone(PhoneClaim.ClaimId("tok-a")) },
            (_, _) => throw new PairingRefusedException("não"), _ => added++, () => Now, _folder, timed: false);

        Assert.True(watcher.CheckOnce());

        Assert.Equal(0, added);
        Assert.Single(PendingPhoneClaimsFile.LoadValid(Now, _folder));
    }

    [Fact]
    public void ReplacingSameName_swaps_the_phone_but_not_a_computer()
    {
        var oldPhone = new KnownMachine("Celular 07", "10.0.0.1", new string('1', 40), Agent, null, DeviceKind.Android);
        var pcSameName = new KnownMachine("Celular 07", "10.0.0.2", new string('2', 40), null);
        var other = new KnownMachine("Celular 08", "10.0.0.3", new string('3', 40), Agent, null, DeviceKind.Android);

        var result = PhoneJoinRoster.ReplacingSameName(new[] { oldPhone, pcSameName, other }, Joined());

        Assert.Equal(new[] { pcSameName, other, Joined() }, result);
    }

    [Fact]
    public void One_token_is_claimed_once_per_round_even_if_two_devices_advertise_it()
    {
        PendingPhoneClaimsFile.Add(new PendingPhoneClaim("07", "tok-a", Now.AddHours(1)), _folder);
        var id = PhoneClaim.ClaimId("tok-a");
        var other = Phone(id) with { AgentThumbprint = new string('B', 40), Address = "10.0.0.8" };
        var claims = 0;
        var added = 0;
        using var watcher = new PhoneClaimWatcher(
            () => new[] { Phone(id), other }, (_, _) => { claims++; return Joined(); }, _ => added++, () => Now, _folder, timed: false);

        watcher.CheckOnce();

        Assert.Equal(1, claims);
        Assert.Equal(1, added);
    }

    [Fact]
    public void Dispose_during_the_search_stops_the_round_before_any_claim()
    {
        PendingPhoneClaimsFile.Add(new PendingPhoneClaim("07", "tok-a", Now.AddHours(1)), _folder);
        PhoneClaimWatcher? watcher = null;
        var claims = 0;
        watcher = new PhoneClaimWatcher(
            () => { watcher!.Dispose(); return new[] { Phone(PhoneClaim.ClaimId("tok-a")) }; },
            (_, _) => { claims++; return Joined(); }, _ => { }, () => Now, _folder, timed: false);

        watcher.CheckOnce();

        Assert.Equal(0, claims);
        Assert.Single(PendingPhoneClaimsFile.LoadValid(Now, _folder));
    }

    [Fact]
    public void Kick_after_Dispose_does_nothing()
    {
        var watcher = new PhoneClaimWatcher(() => Array.Empty<FoundMachine>(), (_, _) => Joined(), _ => { }, () => Now, _folder);
        watcher.Dispose();

        watcher.Kick();
    }

    [Fact]
    public void Kicks_during_a_slow_round_do_not_overlap_but_ask_for_another_round()
    {
        PendingPhoneClaimsFile.Add(new PendingPhoneClaim("07", "tok-a", Now.AddHours(1)), _folder);
        var inside = 0;
        var maxInside = 0;
        var rounds = 0;
        var release = new ManualResetEventSlim();
        var secondRound = new ManualResetEventSlim();
        using var watcher = new PhoneClaimWatcher(
            () =>
            {
                var n = Interlocked.Increment(ref inside);
                maxInside = Math.Max(maxInside, n);
                if (Interlocked.Increment(ref rounds) == 1)
                {
                    release.Wait(TimeSpan.FromSeconds(20));
                }
                else
                {
                    secondRound.Set();
                }
                Interlocked.Decrement(ref inside);
                return Array.Empty<FoundMachine>();
            },
            (_, _) => Joined(), _ => { }, () => Now, _folder);

        watcher.Kick();
        SpinWait.SpinUntil(() => Volatile.Read(ref rounds) == 1, TimeSpan.FromSeconds(10));
        for (var i = 0; i < 5; i++)
        {
            watcher.Kick();
            Thread.Sleep(50);
        }
        Assert.Equal(1, Volatile.Read(ref rounds));
        release.Set();

        Assert.True(secondRound.Wait(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, maxInside);
    }
}
