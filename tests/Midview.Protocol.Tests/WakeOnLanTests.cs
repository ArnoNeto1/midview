using Midview.Protocol;
using Xunit;

public class WakeOnLanTests
{
    private static readonly byte[] Mac = { 0x00, 0x1A, 0x2B, 0x3C, 0x4D, 0x5E };

    [Fact]
    public void MagicPacket_IsSixFFsThenTheAddressSixteenTimes()
    {
        var packet = WakeOnLan.MagicPacket(Mac);

        Assert.Equal(102, packet.Length);
        Assert.All(packet[..6], part => Assert.Equal(0xFF, part));
        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(Mac, packet[(6 + i * 6)..(12 + i * 6)]);
        }
    }

    [Fact]
    public void MagicPacket_RefusesSomethingThatIsNotACardsAddress() =>
        Assert.Throws<ArgumentException>(() => WakeOnLan.MagicPacket(new byte[6]));

    [Fact]
    public void Format_IsTheWayWindowsShowsIt() =>
        Assert.Equal("00-1A-2B-3C-4D-5E", WakeOnLan.FormatMacAddress(Mac));

    [Theory]
    [InlineData("00-1A-2B-3C-4D-5E")]
    [InlineData("00:1a:2b:3c:4d:5e")]
    [InlineData(" 001A2B3C4D5E ")]
    public void Parse_AcceptsTheCommonWritings(string text)
    {
        Assert.True(WakeOnLan.TryParseMacAddress(text, out var parsed));
        Assert.Equal(Mac, parsed);
        Assert.Equal("00-1A-2B-3C-4D-5E", WakeOnLan.NormalizeMacAddress(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("00-1A-2B-3C-4D")] // too short
    [InlineData("00-1A-2B-3C-4D-5E-6F")] // too long
    [InlineData("00-1A-2B-3C-4D-ZZ")] // not hex
    [InlineData("00-00-00-00-00-00")] // no address at all
    [InlineData("FF-FF-FF-FF-FF-FF")] // broadcast
    [InlineData("01-00-5E-00-00-01")] // multicast
    public void Parse_RefusesAnythingElse(string? text)
    {
        Assert.False(WakeOnLan.TryParseMacAddress(text, out _));
        Assert.Null(WakeOnLan.NormalizeMacAddress(text));
    }
}
