using System.Buffers.Binary;
using System.Text;
using Midview.Protocol;
using Xunit;

public class ClassHomeTests
{
    private static readonly string Thumbprint = "0123456789ABCDEF0123456789ABCDEF01234567";

    // When that PC became the home: 21 Oct 2026, in UTC ticks.
    internal const long Generation = 639_000_000_000_000_000;

    private static ClassHome Home(
        string? thumbprint = null, string name = "PC-PROF-1", string address = "192.168.1.20", int port = ClassFolderRemote.Port,
        long generation = Generation) =>
        new(thumbprint ?? Thumbprint, name, address, port, generation);

    private static byte[] Text(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var buffer = new byte[2 + bytes.Length];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)bytes.Length);
        bytes.CopyTo(buffer, 2);
        return buffer;
    }

    internal static byte[] Int64(long value)
    {
        var buffer = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        return buffer;
    }

    // The wire layout written out by hand, so a change to it fails here:
    // version, thumbprint, name, address (each uint16 length + UTF-8), port, generation.
    private static byte[] Raw(
        byte version = 2, string? thumbprint = null, byte[]? name = null, string address = "192.168.1.20", ushort port = 28903,
        long generation = Generation)
    {
        var parts = new List<byte[]>
        {
            new[] { version },
            Text(thumbprint ?? Thumbprint),
            name ?? Text("PC-PROF-1"),
            Text(address),
            new byte[] { (byte)(port >> 8), (byte)port },
            Int64(generation),
        };
        return parts.SelectMany(part => part).ToArray();
    }

    [Fact]
    public void EncodeThenDecode_RoundTrips()
    {
        var home = Home();

        Assert.True(ClassHome.TryDecode(home.Encode(), out var decoded));
        Assert.Equal(home, decoded);
    }

    [Fact]
    public void Encode_MatchesTheDocumentedLayout()
    {
        Assert.Equal(Raw(), Home().Encode());
        Assert.Equal(
            Raw(thumbprint: Thumbprint.ToLowerInvariant(), name: Text("Sala João"), address: "fe80::1", port: 80),
            Home(Thumbprint.ToLowerInvariant(), "Sala João", "fe80::1", 80).Encode());
    }

    [Fact]
    public void EncodeThenDecode_AccentedNameAndIPv6Address_RoundTrips()
    {
        var home = Home(name: "Sala João – 2º andar", address: "fe80::1ff:fe23:4567:890a");

        Assert.True(ClassHome.TryDecode(home.Encode(), out var decoded));
        Assert.Equal(home, decoded);
    }

    [Fact]
    public void EncodeThenDecode_EmptyName_RoundTrips()
    {
        // What an Agent records for a Master that sent no claim.
        var home = Home(name: "");

        Assert.True(ClassHome.TryDecode(home.Encode(), out var decoded));
        Assert.Equal(home, decoded);
    }

    [Fact]
    public void EncodeThenDecode_NameAtTheLimit_RoundTrips()
    {
        var home = Home(name: new string('ã', ClassHome.MaxNameLength));

        Assert.True(ClassHome.TryDecode(home.Encode(), out var decoded));
        Assert.Equal(home, decoded);
    }

    [Fact]
    public void EncodeThenDecode_LowestAndHighestPort_RoundTrip()
    {
        foreach (var port in new[] { 1, 65535 })
        {
            Assert.True(ClassHome.TryDecode(Home(port: port).Encode(), out var decoded));
            Assert.Equal(port, decoded!.Port);
        }
    }

    [Fact]
    public void EncodeThenDecode_LowestAndHighestGeneration_RoundTrip()
    {
        // 0: a home recorded from a lesson start with no claim.
        foreach (var generation in new[] { 0L, 1L, long.MaxValue })
        {
            Assert.True(ClassHome.TryDecode(Home(generation: generation).Encode(), out var decoded));
            Assert.Equal(Home(generation: generation), decoded);
        }
    }

    // The latest generation is the last tick a clock can show; the one after
    // a generation never goes past it.
    [Fact]
    public void TheLatestGeneration_IsTheLastTickOfAClock_AndNextStopsThere()
    {
        Assert.Equal(ClassHome.MaxGeneration, DateTime.MaxValue.Ticks);
        Assert.Equal(6L, ClassHome.Next(5));
        Assert.Equal(ClassHome.MaxGeneration, ClassHome.Next(ClassHome.MaxGeneration - 1));
        Assert.Equal(ClassHome.MaxGeneration, ClassHome.Next(ClassHome.MaxGeneration));
        Assert.Equal(ClassHome.MaxGeneration, ClassHome.Next(long.MaxValue));
    }

    [Fact]
    public void Encode_TheGeneration_IsTheLastEightBytes_BigEndian()
    {
        var payload = Home(generation: 0x0102030405060708).Encode();

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, payload[^8..]);
    }

    [Fact]
    public void None_IsNoHome_AndEncodesEmpty()
    {
        Assert.Null(ClassHome.None);
        Assert.Empty(ClassHome.Encode(ClassHome.None));
        Assert.Equal(Home().Encode(), ClassHome.Encode(Home()));
    }

    [Fact]
    public void TryDecode_EmptyPayload_IsNoHome()
    {
        Assert.True(ClassHome.TryDecode(Array.Empty<byte>(), out var home));
        Assert.Null(home);
    }

    [Fact]
    public void IsSameIdentity_SameThumbprintInAnotherCase_IsTrue()
    {
        var home = Home(Thumbprint.ToLowerInvariant());

        Assert.True(home.IsSameIdentity(Thumbprint, "PC-PROF-1"));
        Assert.True(Home().IsSameIdentity(Thumbprint.ToLowerInvariant(), "PC-PROF-1"));
    }

    [Fact]
    public void IsSameIdentity_NameCaseDoesNotMatter()
    {
        Assert.True(Home(name: "PC-PROF-1").IsSameIdentity(Thumbprint, "pc-prof-1"));
    }

    [Fact]
    public void IsSameIdentity_SameThumbprintAnotherName_IsFalse()
    {
        // Two teacher PCs can share one teacher identity.
        Assert.False(Home(name: "PC-PROF-1").IsSameIdentity(Thumbprint, "PC-PROF-2"));
        Assert.False(Home(name: "PC-PROF-1").IsSameIdentity(Thumbprint, ""));
    }

    [Fact]
    public void IsSameIdentity_AnotherThumbprint_IsFalse()
    {
        var other = new string('A', 40);

        Assert.False(Home().IsSameIdentity(other, "PC-PROF-1"));
        Assert.False(Home(name: "").IsSameIdentity(other, "PC-PROF-1"));
    }

    [Fact]
    public void IsSameIdentity_EmptyRecordedName_MatchesAnyName()
    {
        // A home recorded for a Master from before this: the first claim by
        // that thumbprint fills the name in.
        var home = Home(name: "");

        Assert.True(home.IsSameIdentity(Thumbprint, "PC-PROF-1"));
        Assert.True(home.IsSameIdentity(Thumbprint.ToLowerInvariant(), "OUTRO-PC"));
        Assert.True(home.IsSameIdentity(Thumbprint, ""));
    }

    [Fact]
    public void IsSamePc_TheSameIdentity_IsTrue_WhateverTheGeneration()
    {
        Assert.True(Home().IsSamePc(Thumbprint.ToLowerInvariant(), "pc-prof-1", null));
        Assert.True(Home().IsSamePc(Thumbprint, "PC-PROF-1", Generation + 1));
        Assert.True(Home(name: "").IsSamePc(Thumbprint, "OUTRO-PC", null));
    }

    // The home PC renamed: its certificate and its generation, under a new name.
    [Fact]
    public void IsSamePc_SameThumbprintAndGeneration_UnderANewName_IsTrue()
    {
        Assert.True(Home().IsSamePc(Thumbprint, "PC-PROF-NOVO", Generation));
    }

    // Two teacher PCs sharing one certificate: each became the home at its own moment.
    [Fact]
    public void IsSamePc_SameThumbprint_AnotherNameAndGeneration_IsFalse()
    {
        Assert.False(Home().IsSamePc(Thumbprint, "PC-PROF-2", Generation + 1));
        Assert.False(Home().IsSamePc(Thumbprint, "PC-PROF-2", null));
    }

    // Ruling 5: the home's name under another certificate (Midview reinstalled).
    [Fact]
    public void IsSamePc_TheHomesNameUnderAnotherThumbprint_IsTrue_ButNotAnEmptyName()
    {
        var other = new string('A', 40);

        Assert.True(Home().IsSamePc(other, "pc-prof-1", null));
        Assert.False(Home(name: "").IsSamePc(other, "", null));
        Assert.False(Home().IsSamePc(other, "PC-PROF-2", Generation));
    }

    [Fact]
    public void RefusalText_NamesTheHome_OrAnotherTeacher()
    {
        Assert.Equal("As pastas das turmas ficam no PC PC-PROF-1", Home().RefusalText);
        Assert.Equal("As pastas das turmas ficam no PC de outro professor", Home(name: "").RefusalText);
        Assert.StartsWith(ClassHomeClaim.OtherHomeText, Home().RefusalText);
    }

    [Fact]
    public void Encode_WhatDecodingWouldRefuse_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Home(generation: -1).Encode());
        Assert.Throws<InvalidOperationException>(() => Home(thumbprint: new string('A', 39)).Encode());
        Assert.Throws<InvalidOperationException>(() => Home(thumbprint: new string('G', 40)).Encode());
        Assert.Throws<InvalidOperationException>(() => Home(name: new string('a', ClassHome.MaxNameLength + 1)).Encode());
        Assert.Throws<InvalidOperationException>(() => Home(address: "not an address").Encode());
        Assert.Throws<InvalidOperationException>(() => Home(address: "").Encode());
        Assert.Throws<InvalidOperationException>(() => Home(port: 0).Encode());
        Assert.Throws<InvalidOperationException>(() => Home(port: 65536).Encode());
        Assert.Throws<InvalidOperationException>(() => (Home() with { Name = null! }).Encode());
    }

    [Theory]
    [InlineData("version")]
    [InlineData("formerVersion")]
    [InlineData("noGeneration")]
    [InlineData("negativeGeneration")]
    [InlineData("thumbprintTooShort")]
    [InlineData("thumbprintNotHex")]
    [InlineData("thumbprintEmpty")]
    [InlineData("longName")]
    [InlineData("invalidUtf8Name")]
    [InlineData("addressNotAnAddress")]
    [InlineData("addressEmpty")]
    [InlineData("portZero")]
    [InlineData("trailing")]
    [InlineData("truncated")]
    [InlineData("noPort")]
    [InlineData("nameLengthPastTheEnd")]
    public void TryDecode_RejectsWhatIsNotAHome(string kind)
    {
        var payload = kind switch
        {
            "version" => Raw(version: 3),
            // Before the generation: version 1, ending at the port.
            "formerVersion" => Raw(version: 1)[..^8],
            "noGeneration" => Raw()[..^8],
            "negativeGeneration" => Raw(generation: -1),
            "thumbprintTooShort" => Raw(thumbprint: new string('A', 39)),
            "thumbprintNotHex" => Raw(thumbprint: new string('G', 40)),
            "thumbprintEmpty" => Raw(thumbprint: ""),
            "longName" => Raw(name: Text(new string('a', ClassHome.MaxNameLength + 1))),
            "invalidUtf8Name" => Raw(name: new byte[] { 0, 3, 0x4F, 0xC3, 0x28 }),
            "addressNotAnAddress" => Raw(address: "sala-3"),
            "addressEmpty" => Raw(address: ""),
            "portZero" => Raw(port: 0),
            "trailing" => Raw().Append((byte)0).ToArray(),
            "truncated" => Raw()[..^1],
            "noPort" => Raw()[..^10],
            "nameLengthPastTheEnd" => Raw(name: new byte[] { 0, 200, 0x41 }),
            _ => throw new ArgumentException(kind),
        };

        Assert.False(ClassHome.TryDecode(payload, out var home));
        Assert.Null(home);
    }
}

public class ClassHomeClaimTests
{
    private const long Generation = ClassHomeTests.Generation;

    // version, flags, port, generation, name (uint16 length + UTF-8).
    private static byte[] Raw(byte version = 2, byte flags = 0, ushort port = 28903, byte[]? name = null, long generation = Generation)
    {
        var text = name ?? new byte[] { 0, 9 }.Concat(Encoding.UTF8.GetBytes("PC-PROF-1")).ToArray();
        return new byte[] { version, flags, (byte)(port >> 8), (byte)port }
            .Concat(ClassHomeTests.Int64(generation)).Concat(text).ToArray();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EncodeThenDecode_RoundTrips(bool takeOver)
    {
        var claim = new ClassHomeClaim("PC-PROF-1", ClassFolderRemote.Port, takeOver, Generation);

        Assert.True(ClassHomeClaim.TryDecode(claim.Encode(), out var decoded));
        Assert.Equal(claim, decoded);
    }

    [Fact]
    public void Encode_MatchesTheDocumentedLayout()
    {
        Assert.Equal(Raw(flags: 0), new ClassHomeClaim("PC-PROF-1", 28903, false, Generation).Encode());
        Assert.Equal(Raw(flags: 1), new ClassHomeClaim("PC-PROF-1", 28903, true, Generation).Encode());
        Assert.Equal(
            new byte[] { 2, 1, 0, 80, 1, 2, 3, 4, 5, 6, 7, 8, 0, 0 },
            new ClassHomeClaim("", 80, true, 0x0102030405060708).Encode());
    }

    [Fact]
    public void EncodeThenDecode_AccentedNameAtTheLimit_AndTheHighestGeneration_RoundTrip()
    {
        var claim = new ClassHomeClaim(new string('ã', ClassHome.MaxNameLength), 1, true, long.MaxValue);

        Assert.True(ClassHomeClaim.TryDecode(claim.Encode(), out var decoded));
        Assert.Equal(claim, decoded);
    }

    [Fact]
    public void OtherHomeText_IsTheTextTheMasterRecognises()
    {
        Assert.Equal("As pastas das turmas ficam no PC ", ClassHomeClaim.OtherHomeText);
        Assert.StartsWith(ClassHomeClaim.OtherHomeText, ClassHomeClaim.OtherHomeText + "PC-PROF-1");
    }

    [Fact]
    public void Encode_WhatDecodingWouldRefuse_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => new ClassHomeClaim(new string('a', ClassHome.MaxNameLength + 1), 28903, false, Generation).Encode());
        Assert.Throws<InvalidOperationException>(() => new ClassHomeClaim("PC", 0, false, Generation).Encode());
        Assert.Throws<InvalidOperationException>(() => new ClassHomeClaim("PC", 65536, false, Generation).Encode());
        Assert.Throws<InvalidOperationException>(() => new ClassHomeClaim(null!, 28903, false, Generation).Encode());
        // A claim always says when its PC became the home.
        Assert.Throws<InvalidOperationException>(() => new ClassHomeClaim("PC", 28903, false, 0).Encode());
        Assert.Throws<InvalidOperationException>(() => new ClassHomeClaim("PC", 28903, false, -1).Encode());
    }

    [Theory]
    [InlineData("version")]
    [InlineData("formerVersion")]
    [InlineData("generationZero")]
    [InlineData("negativeGeneration")]
    [InlineData("unknownFlag")]
    [InlineData("portZero")]
    [InlineData("longName")]
    [InlineData("invalidUtf8Name")]
    [InlineData("trailing")]
    [InlineData("truncated")]
    [InlineData("nameLengthPastTheEnd")]
    [InlineData("empty")]
    public void TryDecode_RejectsWhatIsNotAClaim(string kind)
    {
        var payload = kind switch
        {
            "version" => Raw(version: 3),
            // Before the generation: version 1, the name straight after the port.
            "formerVersion" => new byte[] { 1, 0, 0x71, 0x27, 0, 2, 0x50, 0x43 },
            "generationZero" => Raw(generation: 0),
            "negativeGeneration" => Raw(generation: -1),
            "unknownFlag" => Raw(flags: 2),
            "portZero" => Raw(port: 0),
            "longName" => Raw(name: new byte[] { 0, 65 }.Concat(Enumerable.Repeat((byte)'a', 65)).ToArray()),
            "invalidUtf8Name" => Raw(name: new byte[] { 0, 3, 0x4F, 0xC3, 0x28 }),
            "trailing" => Raw().Append((byte)0).ToArray(),
            "truncated" => Raw()[..^1],
            "nameLengthPastTheEnd" => Raw(name: new byte[] { 0, 200, 0x41 }),
            "empty" => Array.Empty<byte>(),
            _ => throw new ArgumentException(kind),
        };

        Assert.False(ClassHomeClaim.TryDecode(payload, out var claim));
        Assert.Null(claim);
    }
}
