using Midview.Protocol;
using Xunit;

public class PhoneAppPolicyTests
{
    // Same bytes as PhoneAppPolicyTest.kt (the Android agent).
    private const string BlockListHex =
        "01" + "000000006553f100" + "01" + "0002"
        + "1a636f6d2e676f6f676c652e616e64726f69642e796f7574756265"
        + "0c636f6d2e7768617473617070";
    private const string LiftedHex = "01" + "000000006553f100" + "00" + "0000";
    private const string AllowOnlyHex = "01" + "000000006553f101" + "02" + "0001" + "11636f6d2e6578616d706c652e6170705f31";
    private const string Header = "01" + "000000006553f100";

    private static byte[] Hex(string text) => Convert.FromHexString(text);

    [Fact]
    public void MessageNumbersAreTheOnesTheAndroidAgentUses()
    {
        Assert.Equal(93, (int)MessageType.SetPhoneAppBlock);
        Assert.Equal(94, (int)MessageType.GetPhoneAppBlock);
        Assert.Equal(95, (int)MessageType.PhoneAppBlockStatus);
        Assert.Equal(96, (int)MessageType.GetPhoneApps);
        Assert.Equal(97, (int)MessageType.PhoneApps);
    }

    [Fact]
    public void Encode_MatchesTheGoldenBytes()
    {
        var policy = new PhoneAppPolicy(1_700_000_000, AppBlockMode.BlockList, new[] { "com.google.android.youtube", "com.whatsapp" });
        Assert.Equal(Hex(BlockListHex), policy.Encode());
        Assert.Equal(Hex(LiftedHex), PhoneAppPolicy.Lifted(1_700_000_000).Encode());
        Assert.Equal(Hex(AllowOnlyHex), new PhoneAppPolicy(1_700_000_001, AppBlockMode.AllowOnly, new[] { "com.example.app_1" }).Encode());
    }

    [Fact]
    public void Decode_ReadsTheGoldenBytes()
    {
        var policy = PhoneAppPolicy.Decode(Hex(BlockListHex));
        Assert.NotNull(policy);
        Assert.Equal(1_700_000_000, policy!.SavedAt);
        Assert.Equal(AppBlockMode.BlockList, policy.Mode);
        Assert.Equal(new[] { "com.google.android.youtube", "com.whatsapp" }, policy.Packages);
        Assert.False(PhoneAppPolicy.Decode(Hex(LiftedHex))!.IsActive);
    }

    [Theory]
    [InlineData("com.whatsapp", true)]
    [InlineData("com.example.app_1", true)]
    [InlineData("A.b", true)]
    [InlineData("br.gov.Escola2", true)]
    [InlineData("whatsapp", false)]
    [InlineData("com.", false)]
    [InlineData(".com.x", false)]
    [InlineData("1com.x", false)]
    [InlineData("com..x", false)]
    [InlineData("com.x-y", false)]
    [InlineData("com.x\n", false)]
    [InlineData("com.açai", false)]
    [InlineData("chrome.exe ", false)]
    [InlineData("", false)]
    public void IsValidPackageName_FollowsTheAndroidPattern(string name, bool expected) =>
        Assert.Equal(expected, PhoneAppPolicy.IsValidPackageName(name));

    [Fact]
    public void IsValidPackageName_RejectsNullAndTooLong()
    {
        Assert.False(PhoneAppPolicy.IsValidPackageName(null));
        Assert.False(PhoneAppPolicy.IsValidPackageName("a." + new string('b', 254)));
        Assert.True(PhoneAppPolicy.IsValidPackageName("a." + new string('b', 253)));
    }

    [Fact]
    public void Encode_RefusesInvalidPolicies()
    {
        Assert.Throws<InvalidOperationException>(() => new PhoneAppPolicy(1, AppBlockMode.BlockList, new[] { "chrome.exe " }).Encode());
        Assert.Throws<InvalidOperationException>(() => new PhoneAppPolicy(1, AppBlockMode.BlockList, new[] { "com.a", "com.a" }).Encode());
        Assert.Throws<InvalidOperationException>(() => new PhoneAppPolicy(1, AppBlockMode.None, new[] { "com.a" }).Encode());
        Assert.Throws<InvalidOperationException>(() => new PhoneAppPolicy(1, (AppBlockMode)9, Array.Empty<string>()).Encode());
        Assert.Throws<InvalidOperationException>(() =>
            new PhoneAppPolicy(1, AppBlockMode.BlockList, Enumerable.Range(0, 201).Select(i => $"com.app{i}").ToList()).Encode());
    }

    [Fact]
    public void TwoHundredPackages_RoundTrip()
    {
        var names = Enumerable.Range(0, 200).Select(i => $"com.app{i}").ToList();
        var back = PhoneAppPolicy.Decode(new PhoneAppPolicy(5, AppBlockMode.AllowOnly, names).Encode());
        Assert.Equal(names, back!.Packages);
    }

    [Fact]
    public void Decode_RejectsTrailingBytes() =>
        Assert.Null(PhoneAppPolicy.Decode(Hex(BlockListHex + "00")));

    [Fact]
    public void Decode_RejectsTruncated() =>
        Assert.Null(PhoneAppPolicy.Decode(Hex(BlockListHex[..^2])));

    [Fact]
    public void Decode_RejectsABadVersionOrShortHeader()
    {
        Assert.Null(PhoneAppPolicy.Decode(Hex("02" + BlockListHex[2..])));
        Assert.Null(PhoneAppPolicy.Decode(Array.Empty<byte>()));
        Assert.Null(PhoneAppPolicy.Decode(Hex("01000000006553f10001")));
    }

    [Fact]
    public void Decode_RejectsZeroLengthNamesBadUtf8AndDuplicates()
    {
        Assert.Null(PhoneAppPolicy.Decode(Hex(Header + "01" + "0001" + "00")));
        Assert.Null(PhoneAppPolicy.Decode(Hex(Header + "01" + "0001" + "03" + "c328c3")));
        Assert.Null(PhoneAppPolicy.Decode(Hex(Header + "01" + "0002" + "05636f6d2e61" + "05636f6d2e61")));
    }

    [Fact]
    public void Decode_RejectsAnInvalidPackageNameAndAnUnknownMode()
    {
        Assert.Null(PhoneAppPolicy.Decode(Hex(Header + "01" + "0001" + "08" + "7768617473617070")));
        Assert.Null(PhoneAppPolicy.Decode(Hex(Header + "07" + "0000")));
    }

    [Fact]
    public void Decode_RejectsLiftedOrNoneWithNamesAndTooManyNames()
    {
        Assert.Null(PhoneAppPolicy.Decode(Hex(Header + "00" + "0001" + "05636f6d2e61")));
        Assert.Null(PhoneAppPolicy.Decode(Hex(Header + "01" + "00c9")));
    }

    [Fact]
    public void SameRulesAs_IgnoresOrderAndSavedAt()
    {
        var a = new PhoneAppPolicy(1, AppBlockMode.BlockList, new[] { "com.a", "com.b" });
        Assert.True(a.SameRulesAs(new PhoneAppPolicy(9, AppBlockMode.BlockList, new[] { "com.b", "com.a" })));
        Assert.False(a.SameRulesAs(new PhoneAppPolicy(1, AppBlockMode.AllowOnly, new[] { "com.a", "com.b" })));
        Assert.False(a.SameRulesAs(new PhoneAppPolicy(1, AppBlockMode.BlockList, new[] { "com.a", "com.c" })));
        Assert.False(a.SameRulesAs(new PhoneAppPolicy(1, AppBlockMode.BlockList, new[] { "com.a" })));
        Assert.True(PhoneAppPolicy.Lifted(1).SameRulesAs(PhoneAppPolicy.Lifted(2)));
    }
}
