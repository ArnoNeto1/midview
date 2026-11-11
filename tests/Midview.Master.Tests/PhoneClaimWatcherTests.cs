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
}
