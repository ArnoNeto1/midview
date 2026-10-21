using System.Buffers.Binary;
using System.Text;
using Midview.Protocol;
using Xunit;

public class ClassHomeTests
{
    private static readonly string Thumbprint = "0123456789ABCDEF0123456789ABCDEF01234567";

    private static ClassHome Home(
        string? thumbprint = null, string name = "PC-PROF-1", string address = "192.168.1.20", int port = ClassFolderRemote.Port) =>
        new(thumbprint ?? Thumbprint, name, address, port);

    private static byte[] Text(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var buffer = new byte[2 + bytes.Length];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)bytes.Length);
        bytes.CopyTo(buffer, 2);
        return buffer;
    }

    // The wire layout written out by hand, so a change to it fails here:
    // version, thumbprint, name, address (each uint16 length + UTF-8), port.
    private static byte[] Raw(
        byte version = 1, string? thumbprint = null, byte[]? name = null, string address = "192.168.1.20", ushort port = 28903)
    {
        var parts = new List<byte[]>
        {
            new[] { version },
            Text(thumbprint ?? Thumbprint),
            name ?? Text("PC-PROF-1"),
            Text(address),
            new byte[] { (byte)(port >> 8), (byte)port },
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
    public void Encode_WhatDecodingWouldRefuse_Throws()
    {
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
            "version" => Raw(version: 2),
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
            "noPort" => Raw()[..^2],
            "nameLengthPastTheEnd" => Raw(name: new byte[] { 0, 200, 0x41 }),
            _ => throw new ArgumentException(kind),
        };

        Assert.False(ClassHome.TryDecode(payload, out var home));
        Assert.Null(home);
    }
}

public class ClassHomeClaimTests
{
    private static byte[] Raw(byte version = 1, byte flags = 0, ushort port = 28903, byte[]? name = null)
    {
        var text = name ?? new byte[] { 0, 9 }.Concat(Encoding.UTF8.GetBytes("PC-PROF-1")).ToArray();
        return new byte[] { version, flags, (byte)(port >> 8), (byte)port }.Concat(text).ToArray();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EncodeThenDecode_RoundTrips(bool takeOver)
    {
        var claim = new ClassHomeClaim("PC-PROF-1", ClassFolderRemote.Port, takeOver);

        Assert.True(ClassHomeClaim.TryDecode(claim.Encode(), out var decoded));
        Assert.Equal(claim, decoded);
    }

    [Fact]
    public void Encode_MatchesTheDocumentedLayout()
    {
        Assert.Equal(Raw(flags: 0), new ClassHomeClaim("PC-PROF-1", 28903, false).Encode());
        Assert.Equal(Raw(flags: 1), new ClassHomeClaim("PC-PROF-1", 28903, true).Encode());
        Assert.Equal(
            new byte[] { 1, 1, 0, 80, 0, 0 },
            new ClassHomeClaim("", 80, true).Encode());
    }

    [Fact]
    public void EncodeThenDecode_AccentedNameAtTheLimit_RoundTrips()
    {
        var claim = new ClassHomeClaim(new string('ã', ClassHome.MaxNameLength), 1, true);

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
        Assert.Throws<InvalidOperationException>(() => new ClassHomeClaim(new string('a', ClassHome.MaxNameLength + 1), 28903, false).Encode());
        Assert.Throws<InvalidOperationException>(() => new ClassHomeClaim("PC", 0, false).Encode());
        Assert.Throws<InvalidOperationException>(() => new ClassHomeClaim("PC", 65536, false).Encode());
        Assert.Throws<InvalidOperationException>(() => new ClassHomeClaim(null!, 28903, false).Encode());
    }

    [Theory]
    [InlineData("version")]
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
            "version" => Raw(version: 2),
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
