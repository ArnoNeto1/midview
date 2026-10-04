using Midview.Protocol;
using Xunit;

public class AppBlockPolicyTests
{
    [Theory]
    [InlineData("chrome", "chrome.exe")]
    [InlineData("CHROME.EXE", "chrome.exe")]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe", "chrome.exe")]
    [InlineData("  Notepad++.exe  ", "notepad++.exe")]
    [InlineData("game (2).exe", "game (2).exe")]
    public void NormalizeExeName_KeepsTheFileNameOnly(string input, string expected) =>
        Assert.Equal(expected, AppBlockPolicy.NormalizeExeName(input));

    [Theory]
    [InlineData(@"..\x.exe", "x.exe")]
    [InlineData("a/b.exe", "b.exe")]
    public void NormalizeExeName_ARelativePath_KeepsTheLastSegment(string input, string expected) =>
        Assert.Equal(expected, AppBlockPolicy.NormalizeExeName(input));

    [Theory]
    [InlineData("*.exe")]
    [InlineData("chrome.dll")]
    [InlineData("con.exe\nrm.exe")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("folder/")]
    public void NormalizeExeName_RejectsAnythingElse(string input) =>
        Assert.Null(AppBlockPolicy.NormalizeExeName(input));

    [Fact]
    public void RoundTrips_EachMode()
    {
        foreach (var policy in new[]
                 {
                     AppBlockPolicy.Lifted(1000),
                     new AppBlockPolicy(2000, AppBlockMode.BlockList, new[] { "chrome.exe", "roblox.exe" }),
                     new AppBlockPolicy(3000, AppBlockMode.AllowOnly, new[] { "winword.exe" }),
                     new AppBlockPolicy(4000, AppBlockMode.AllowOnly, Array.Empty<string>()),
                 })
        {
            Assert.True(AppBlockPolicy.TryDecode(policy.Encode(), out var decoded));
            Assert.Equal(policy.SavedAt, decoded.SavedAt);
            Assert.Equal(policy.Mode, decoded.Mode);
            Assert.True(policy.SameRulesAs(decoded));
        }
    }

    [Fact]
    public void None_WithPrograms_IsInvalid() =>
        Assert.False(new AppBlockPolicy(1, AppBlockMode.None, new[] { "chrome.exe" }).IsValid());

    [Fact]
    public void AProtectedName_IsInvalid()
    {
        Assert.False(new AppBlockPolicy(1, AppBlockMode.BlockList, new[] { "explorer.exe" }).IsValid());
        Assert.False(new AppBlockPolicy(1, AppBlockMode.BlockList, new[] { "midviewsession.exe" }).IsValid());
    }

    [Fact]
    public void ANonNormalizedName_IsInvalid() =>
        Assert.False(new AppBlockPolicy(1, AppBlockMode.BlockList, new[] { "Chrome" }).IsValid());

    [Fact]
    public void Duplicates_AreInvalid() =>
        Assert.False(new AppBlockPolicy(1, AppBlockMode.BlockList, new[] { "chrome.exe", "chrome.exe" }).IsValid());

    [Fact]
    public void TooMany_AreInvalid() =>
        Assert.False(new AppBlockPolicy(1, AppBlockMode.BlockList,
            Enumerable.Range(0, AppBlockPolicy.MaxPrograms + 1).Select(i => $"p{i}.exe").ToList()).IsValid());

    [Fact]
    public void TryDecode_RejectsGarbage()
    {
        Assert.False(AppBlockPolicy.TryDecode(Array.Empty<byte>(), out _));
        Assert.False(AppBlockPolicy.TryDecode(new byte[] { 9, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0 }, out _)); // bad version
        var valid = new AppBlockPolicy(1, AppBlockMode.BlockList, new[] { "chrome.exe" }).Encode();
        Assert.False(AppBlockPolicy.TryDecode(valid.Append((byte)0).ToArray(), out _)); // trailing byte
    }

    [Fact]
    public void SameRulesAs_IgnoresOrderAndSavedAt()
    {
        var a = new AppBlockPolicy(1, AppBlockMode.BlockList, new[] { "a.exe", "b.exe" });
        var b = new AppBlockPolicy(9, AppBlockMode.BlockList, new[] { "b.exe", "a.exe" });
        Assert.True(a.SameRulesAs(b));
        Assert.False(a.SameRulesAs(a with { Mode = AppBlockMode.AllowOnly }));
    }
}

public class OpenProgramsReportTests
{
    [Fact]
    public void RoundTrips_WithAccentsAndCounts()
    {
        var programs = new[]
        {
            new OpenProgram("chrome.exe", "Google Chrome", 3),
            new OpenProgram("winword.exe", "Documento — Word", 1),
        };

        Assert.True(OpenProgramsReport.TryDecode(OpenProgramsReport.Encode(programs), out var decoded));
        Assert.Equal(2, decoded.Count);
        Assert.Equal("Google Chrome", decoded[0].DisplayName);
        Assert.Equal(3, decoded[0].WindowCount);
        Assert.Equal("Documento — Word", decoded[1].DisplayName);
    }

    [Fact]
    public void CleanDisplayName_DropsControlChars_CapsLength_FallsBack()
    {
        Assert.Equal("A B", OpenProgramsReport.CleanDisplayName("A\tB", "x.exe"));
        Assert.Equal("x.exe", OpenProgramsReport.CleanDisplayName("   ", "x.exe"));
        Assert.True(OpenProgramsReport.CleanDisplayName(new string('a', 200), "x.exe").Length <= OpenProgramsReport.MaxDisplayNameLength);
    }

    [Fact]
    public void Encode_DropsAnInvalidExeName_OnDecode()
    {
        // A forged report with a non-normalized exe name must not decode.
        var bytes = OpenProgramsReport.Encode(new[] { new OpenProgram("chrome.exe", "ok", 1) });
        Assert.True(OpenProgramsReport.TryDecode(bytes, out _));
        var forged = OpenProgramsReport.Encode(new[] { new OpenProgram("chrome.exe", "ok", 1) }).ToList();
        forged[4] = (byte)'/'; // corrupt the exe name's first byte
        Assert.False(OpenProgramsReport.TryDecode(forged.ToArray(), out _));
    }
}
