using System.Text;
using Midview.Protocol;
using Xunit;

public class PhoneOptimizeReportTests
{
    // Same literals as PhoneOptimizeReportTest.kt and NetworkUpdateCodecTest.kt (the Android agent).
    private const string FullJson =
        "{\"optimizedAt\":\"2026-11-12T15:30:45+00:00\",\"hidden\":[\"com.miui.msa.global\",\"com.miui.analytics\"],"
        + "\"failed\":[\"com.xiaomi.mipicks\"],\"dns\":\"on\",\"dnsHost\":\"dns.adguard-dns.com\"}";

    private const string EmptyJson =
        "{\"optimizedAt\":null,\"hidden\":[],\"failed\":[],\"dns\":\"offInternetBlocked\",\"dnsHost\":\"\"}";

    private const string OfferHex =
        "000102030405060708090a0b0c0d0e0f"
        + "101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f"
        + "0000000000a00000"
        + "303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f"
        + "0000000000a00000";

    private const string StatusJson =
        "{\"fingerprint\":\"000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f\",\"build\":\"0.0.51\","
        + "\"ready\":true,\"attempt\":{\"id\":\"000102030405060708090a0b0c0d0e0f\",\"state\":\"committed\","
        + "\"detail\":\"ok\",\"updatedAt\":\"2026-11-12T15:30:45+00:00\"}}";

    private const string StatusNullJson =
        "{\"fingerprint\":null,\"build\":null,\"ready\":false,\"attempt\":null}";

    private static readonly DateTimeOffset At = new(2026, 11, 12, 15, 30, 45, TimeSpan.Zero);

    private static PhoneOptimizeReport Full() => new(
        At, new[] { "com.miui.msa.global", "com.miui.analytics" }, new[] { "com.xiaomi.mipicks" },
        PhoneDnsStates.On, "dns.adguard-dns.com");

    [Fact]
    public void Encode_MatchesTheGoldenJson()
    {
        Assert.Equal(FullJson, Encoding.UTF8.GetString(Full().Encode()));
        var empty = new PhoneOptimizeReport(null, Array.Empty<string>(), Array.Empty<string>(), PhoneDnsStates.OffInternetBlocked, "");
        Assert.Equal(EmptyJson, Encoding.UTF8.GetString(empty.Encode()));
    }

    [Fact]
    public void TryDecode_ReadsTheGoldenJson()
    {
        Assert.True(PhoneOptimizeReport.TryDecode(Encoding.UTF8.GetBytes(FullJson), out var full));
        Assert.Equal(At, full.OptimizedAt);
        Assert.Equal(new[] { "com.miui.msa.global", "com.miui.analytics" }, full.Hidden);
        Assert.Equal(new[] { "com.xiaomi.mipicks" }, full.Failed);
        Assert.Equal("on", full.Dns);
        Assert.Equal("dns.adguard-dns.com", full.DnsHost);

        Assert.True(PhoneOptimizeReport.TryDecode(Encoding.UTF8.GetBytes(EmptyJson), out var empty));
        Assert.Null(empty.OptimizedAt);
        Assert.Empty(empty.Hidden);
        Assert.Empty(empty.Failed);
        Assert.Equal("offInternetBlocked", empty.Dns);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("{\"optimizedAt\":null,\"failed\":[],\"dns\":\"on\",\"dnsHost\":\"\"}")]
    [InlineData("{\"optimizedAt\":null,\"hidden\":[],\"dns\":\"on\",\"dnsHost\":\"\"}")]
    [InlineData("{\"optimizedAt\":null,\"hidden\":[],\"failed\":[],\"dns\":\"maybe\",\"dnsHost\":\"\"}")]
    [InlineData("{\"optimizedAt\":null,\"hidden\":[],\"failed\":[],\"dnsHost\":\"\"}")]
    [InlineData("null")]
    public void TryDecode_RefusesBadJson(string json) =>
        Assert.False(PhoneOptimizeReport.TryDecode(Encoding.UTF8.GetBytes(json), out _));

    [Fact]
    public void TryDecode_RefusesInvalidUtf8() =>
        Assert.False(PhoneOptimizeReport.TryDecode(new byte[] { 0x7b, 0xff, 0xfe, 0x7d }, out _));

    [Fact]
    public void DnsStates_AreTheWireNames()
    {
        Assert.Equal("on", PhoneDnsStates.On);
        Assert.Equal("offNoInternet", PhoneDnsStates.OffNoInternet);
        Assert.Equal("offNetworkBlocks", PhoneDnsStates.OffNetworkBlocks);
        Assert.Equal("offInternetBlocked", PhoneDnsStates.OffInternetBlocked);
        Assert.Equal("offError", PhoneDnsStates.OffError);
        Assert.Equal("offByTeacher", PhoneDnsStates.OffByTeacher);
        Assert.True(PhoneDnsStates.IsKnown(PhoneDnsStates.OffByTeacher));
    }

    [Fact]
    public void MessageTypes_AreTheWireNumbers()
    {
        Assert.Equal(98, (int)MessageType.OptimizePhone);
        Assert.Equal(99, (int)MessageType.PhoneOptimizeReport);
    }

    [Fact]
    public void MaxApkSize_Is64MiB() => Assert.Equal(64L * 1024 * 1024, PhoneUpdate.MaxApkSize);

    [Fact]
    public void Offer_GoldenBytes_AreWhatNetworkUpdateEncodes()
    {
        var id = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var print = Enumerable.Range(16, 32).Select(i => (byte)i).ToArray();
        var sha = Enumerable.Range(48, 32).Select(i => (byte)i).ToArray();
        var offer = new UpdateOffer(id, print, 0xA00000, sha, 0xA00000);
        Assert.Equal(OfferHex, Convert.ToHexString(NetworkUpdate.EncodeOffer(offer)).ToLowerInvariant());
        Assert.True(NetworkUpdate.TryDecodeOffer(Convert.FromHexString(OfferHex), out var back));
        Assert.Equal(0xA00000, back.ZipSize);
    }

    [Fact]
    public void Status_GoldenJson_IsAcceptedAndReproduced()
    {
        Assert.True(UpdateStatusReport.TryDecode(Encoding.UTF8.GetBytes(StatusJson), out var report));
        Assert.Equal("0.0.51", report.Build);
        Assert.True(report.Ready);
        Assert.Equal("committed", report.Attempt!.State);
        Assert.Equal(StatusJson, Encoding.UTF8.GetString(report.Encode()));

        Assert.True(UpdateStatusReport.TryDecode(Encoding.UTF8.GetBytes(StatusNullJson), out var none));
        Assert.Null(none.Attempt);
        Assert.Equal(StatusNullJson, Encoding.UTF8.GetString(none.Encode()));
    }

    [Fact]
    public void Status_KotlinEscapedDetail_IsDecoded()
    {
        // What UpdateStatusReport on the Android agent writes for a detail with
        // a quote, a backslash, a control character and a non-ASCII letter.
        // .NET writes the non-ASCII letter as \u00E7; decoding accepts both.
        const string json =
            "{\"fingerprint\":null,\"build\":null,\"ready\":false,\"attempt\":{\"id\":\"00\",\"state\":\"failed\","
            + "\"detail\":\"a\\\"b\\\\c\\u0001\u00e7\",\"updatedAt\":\"2026-11-12T15:30:45+00:00\"}}";
        Assert.True(UpdateStatusReport.TryDecode(Encoding.UTF8.GetBytes(json), out var report));
        Assert.Equal("a\"b\\c\u0001\u00e7", report.Attempt!.Detail);
    }
}
