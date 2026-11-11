using Midview.Master;
using Xunit;

public class PendingPhoneClaimsFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "midview-claims-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Add_then_LoadValid_returns_the_claim()
    {
        var claim = new PendingPhoneClaim("07", "tok-a", Now.AddHours(1));

        PendingPhoneClaimsFile.Add(claim, _folder);

        Assert.Equal(new[] { claim }, PendingPhoneClaimsFile.LoadValid(Now, _folder));
    }

    [Fact]
    public void Expired_claim_is_not_returned_and_leaves_the_file()
    {
        PendingPhoneClaimsFile.Add(new PendingPhoneClaim("07", "old", Now.AddMinutes(-1)), _folder);
        PendingPhoneClaimsFile.Add(new PendingPhoneClaim("08", "new", Now.AddHours(1)), _folder);

        var valid = PendingPhoneClaimsFile.LoadValid(Now, _folder);

        Assert.Equal(new[] { "new" }, valid.Select(c => c.Token));
        Assert.Equal(new[] { "new" }, PendingPhoneClaimsFile.LoadValid(Now.AddMinutes(-30), _folder).Select(c => c.Token));
    }

    [Fact]
    public void Add_same_number_replaces_the_token()
    {
        PendingPhoneClaimsFile.Add(new PendingPhoneClaim("07", "first", Now.AddHours(1)), _folder);
        PendingPhoneClaimsFile.Add(new PendingPhoneClaim("07", "second", Now.AddHours(1)), _folder);

        var valid = PendingPhoneClaimsFile.LoadValid(Now, _folder);

        Assert.Single(valid);
        Assert.Equal("second", valid[0].Token);
    }

    [Fact]
    public void Remove_drops_only_that_token()
    {
        PendingPhoneClaimsFile.Add(new PendingPhoneClaim("07", "a", Now.AddHours(1)), _folder);
        PendingPhoneClaimsFile.Add(new PendingPhoneClaim("08", "b", Now.AddHours(1)), _folder);

        PendingPhoneClaimsFile.Remove("a", _folder);

        Assert.Equal(new[] { "b" }, PendingPhoneClaimsFile.LoadValid(Now, _folder).Select(c => c.Token));
    }

    [Fact]
    public void Missing_or_corrupt_file_is_an_empty_list()
    {
        Assert.Empty(PendingPhoneClaimsFile.LoadValid(Now, _folder));
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(Path.Combine(_folder, "phone-claims.bin"), new byte[] { 9, 8, 7, 6, 5 });

        Assert.Empty(PendingPhoneClaimsFile.LoadValid(Now, _folder));
    }

    [Fact]
    public void File_is_not_plain_text()
    {
        PendingPhoneClaimsFile.Add(new PendingPhoneClaim("07", "segredo-token", Now.AddHours(1)), _folder);

        foreach (var file in Directory.GetFiles(_folder))
        {
            Assert.DoesNotContain("segredo-token", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)));
        }
    }

    [Fact]
    public void Concurrent_Add_and_Remove_on_different_numbers_lose_nothing()
    {
        for (var round = 0; round < 20; round++)
        {
            var folder = Path.Combine(_folder, "r" + round);
            var kept = Enumerable.Range(0, 15).Select(i => new PendingPhoneClaim("K" + i, "keep-" + i, Now.AddHours(1))).ToList();
            var doomed = Enumerable.Range(0, 15).Select(i => new PendingPhoneClaim("D" + i, "drop-" + i, Now.AddHours(1))).ToList();
            foreach (var c in doomed)
            {
                PendingPhoneClaimsFile.Add(c, folder);
            }

            var adds = Task.Run(() => Parallel.ForEach(kept, c => PendingPhoneClaimsFile.Add(c, folder)));
            var removes = Task.Run(() => Parallel.ForEach(doomed, c => PendingPhoneClaimsFile.Remove(c.Token, folder)));
            Task.WaitAll(adds, removes);

            Assert.Equal(
                kept.Select(c => c.Token).OrderBy(t => t),
                PendingPhoneClaimsFile.LoadValid(Now, folder).Select(c => c.Token).OrderBy(t => t));
        }
    }
}
