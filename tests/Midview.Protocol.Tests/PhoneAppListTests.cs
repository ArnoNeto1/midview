using Midview.Protocol;
using Xunit;

public class PhoneAppListTests
{
    // Same bytes as PhoneAppListTest.kt (the Android agent).
    private const string GoldenHex =
        "01" + "0002"
        + "0c636f6d2e7768617473617070" + "085768617473417070" + "0004" + "89504e47"
        + "07636f6d2e782e79" + "07" + "43c3a26d657261" + "0003" + "010203";

    private static byte[] Hex(string text) => Convert.FromHexString(text);

    private static readonly IReadOnlyList<PhoneApp> Golden = new[]
    {
        new PhoneApp("com.whatsapp", "WhatsApp", new byte[] { 0x89, 0x50, 0x4e, 0x47 }),
        new PhoneApp("com.x.y", "Câmera", new byte[] { 1, 2, 3 }),
    };

    [Fact]
    public void Encode_MatchesTheGoldenBytes() =>
        Assert.Equal(Hex(GoldenHex), PhoneAppList.Encode(Golden));

    [Fact]
    public void Encode_OfNothing_IsVersionAndZeroCount() =>
        Assert.Equal(Hex("010000"), PhoneAppList.Encode(Array.Empty<PhoneApp>()));

    [Fact]
    public void Decode_ReadsTheGoldenBytes()
    {
        var apps = PhoneAppList.Decode(Hex(GoldenHex));
        Assert.NotNull(apps);
        Assert.Equal(2, apps!.Count);
        Assert.Equal("com.whatsapp", apps[0].Package);
        Assert.Equal("Câmera", apps[1].Label);
        Assert.Equal(new byte[] { 1, 2, 3 }, apps[1].IconPng);
        Assert.Empty(PhoneAppList.Decode(Hex("010000"))!);
    }

    [Fact]
    public void IconLengthIsBigEndianAndCanBeLarge()
    {
        var icon = new byte[0x0102];
        var bytes = PhoneAppList.Encode(new[] { new PhoneApp("com.a.b", "A", icon) });
        // 01 | 0001 | 07 + 7 bytes | 01 + 1 byte | then the icon length: 01 02
        Assert.Equal(new byte[] { 0x01, 0x02 }, bytes[(3 + 8 + 2)..(3 + 8 + 4)]);
        Assert.Equal(icon.Length, PhoneAppList.Decode(bytes)![0].IconPng.Length);
    }

    [Fact]
    public void ThreeHundredApps_RoundTrip_AndMoreAreRefused()
    {
        var apps = Enumerable.Range(0, 300).Select(i => new PhoneApp($"com.app{i}", $"App {i}", new byte[] { 1 })).ToList();
        Assert.Equal(300, PhoneAppList.Decode(PhoneAppList.Encode(apps))!.Count);
        apps.Add(new PhoneApp("com.app300", "App 300", new byte[] { 1 }));
        Assert.Throws<InvalidOperationException>(() => PhoneAppList.Encode(apps));
        Assert.Null(PhoneAppList.Decode(Hex("01" + "012d")));
    }

    [Fact]
    public void Label_Of64CharsIsKept_Of65IsRefused()
    {
        var ok = PhoneAppList.Decode(PhoneAppList.Encode(new[] { new PhoneApp("com.a.b", new string('x', 64), Array.Empty<byte>()) }));
        Assert.Equal(64, ok![0].Label.Length);
        Assert.Throws<InvalidOperationException>(() => PhoneAppList.Encode(new[] { new PhoneApp("com.a.b", new string('x', 65), Array.Empty<byte>()) }));
        // A hand-built payload with a 65-char label.
        var payload = Hex("01" + "0001" + "07636f6d2e612e62" + "41" + new string('7', 0) + string.Concat(Enumerable.Repeat("78", 65)) + "0000");
        Assert.Null(PhoneAppList.Decode(payload));
    }

    [Fact]
    public void Encode_RefusesBadPackagesDuplicatesAndHugeIcons()
    {
        Assert.Throws<InvalidOperationException>(() => PhoneAppList.Encode(new[] { new PhoneApp("chrome.exe ", "x", Array.Empty<byte>()) }));
        Assert.Throws<InvalidOperationException>(() => PhoneAppList.Encode(new[]
        {
            new PhoneApp("com.a.b", "x", Array.Empty<byte>()), new PhoneApp("com.a.b", "y", Array.Empty<byte>()),
        }));
        Assert.Throws<InvalidOperationException>(() => PhoneAppList.Encode(new[] { new PhoneApp("com.a.b", "x", new byte[65536]) }));
    }

    [Fact]
    public void Decode_RejectsTrailingBytesTruncationAndBadVersion()
    {
        Assert.Null(PhoneAppList.Decode(Hex(GoldenHex + "00")));
        Assert.Null(PhoneAppList.Decode(Hex(GoldenHex[..^2])));
        Assert.Null(PhoneAppList.Decode(Hex("03" + GoldenHex[2..])));
        Assert.Null(PhoneAppList.Decode(Array.Empty<byte>()));
        Assert.Null(PhoneAppList.Decode(Hex("0100")));
    }

    [Fact]
    public void Decode_RejectsBadPackageBadUtf8AndDuplicates()
    {
        // package "whatsapp" (no dot)
        Assert.Null(PhoneAppList.Decode(Hex("01" + "0001" + "08" + "7768617473617070" + "00" + "0000")));
        // zero-length package
        Assert.Null(PhoneAppList.Decode(Hex("01" + "0001" + "00" + "00" + "0000")));
        // label with invalid UTF-8
        Assert.Null(PhoneAppList.Decode(Hex("01" + "0001" + "07636f6d2e612e62" + "02c328" + "0000")));
        // duplicate package
        var one = "07636f6d2e612e62" + "00" + "0000";
        Assert.Null(PhoneAppList.Decode(Hex("01" + "0002" + one + one)));
    }

    // Format 2, each app's flags after its icon: the same bytes as PhoneAppListTest.kt.
    private const string FlaggedHex =
        "02" + "0002"
        + "0c636f6d2e7768617473617070" + "085768617473417070" + "0004" + "89504e47" + "01"
        + "07636f6d2e782e79" + "07" + "43c3a26d657261" + "0003" + "010203" + "02";

    private static readonly IReadOnlyList<PhoneApp> Flagged = new[]
    {
        new PhoneApp("com.whatsapp", "WhatsApp", new byte[] { 0x89, 0x50, 0x4e, 0x47 }, PhoneAppFlags.System),
        new PhoneApp("com.x.y", "Câmera", new byte[] { 1, 2, 3 }, PhoneAppFlags.Blocked),
    };

    [Fact]
    public void TheListWithFlags_MatchesTheGoldenBytes()
    {
        Assert.Equal(Hex(FlaggedHex), PhoneAppList.Encode(Flagged, PhoneAppList.WithFlags));
        var apps = PhoneAppList.Decode(Hex(FlaggedHex))!;
        Assert.Equal(new[] { PhoneAppFlags.System, PhoneAppFlags.Blocked }, apps.Select(app => app.Flags));
        Assert.Equal("Câmera", apps[1].Label);
        // Format 1 carries no flags, and reads as None.
        Assert.Equal(Hex(GoldenHex), PhoneAppList.Encode(Flagged));
        Assert.All(PhoneAppList.Decode(Hex(GoldenHex))!, app => Assert.Equal(PhoneAppFlags.None, app.Flags));
    }

    [Fact]
    public void TheListWithFlags_RefusesAMissingFlagOrAnUnknownFormat()
    {
        Assert.Null(PhoneAppList.Decode(Hex(FlaggedHex[..^2])));
        Assert.Null(PhoneAppList.Decode(Hex(FlaggedHex + "00")));
        Assert.Throws<InvalidOperationException>(() => PhoneAppList.Encode(Flagged, 3));
        var ola = new PhoneApp("com.x.ola", "Olá", Array.Empty<byte>(), PhoneAppFlags.InstalledByMidview | PhoneAppFlags.Blocked);
        Assert.Equal(ola.Flags, PhoneAppList.Decode(PhoneAppList.Encode(new[] { ola }, PhoneAppList.WithFlags))!.Single().Flags);
    }

    [Fact]
    public void TheRequestForFlags_IsTheOneByteTwo() =>
        Assert.Equal(new byte[] { 2 }, PhoneAppList.WithFlagsRequest);
}
