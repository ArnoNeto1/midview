using Midview.Protocol;
using Xunit;

// "Tocar para encontrar": message 108, the seconds as two bytes (the same
// vectors as PhoneFindTest.kt).
public class PhoneFindTests
{
    [Fact]
    public void MessageType_IsTheWireNumber() => Assert.Equal(108, (int)MessageType.FindPhone);

    [Theory]
    [InlineData(0, new byte[] { 0x00, 0x00 })]
    [InlineData(15, new byte[] { 0x00, 0x0F })]
    [InlineData(60, new byte[] { 0x00, 0x3C })]
    [InlineData(300, new byte[] { 0x01, 0x2C })]
    public void Seconds_round_trip(int seconds, byte[] wire)
    {
        Assert.Equal(wire, PhoneFind.Encode(seconds));
        Assert.Equal(seconds, PhoneFind.Decode(wire));
    }

    [Fact]
    public void Out_of_range_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PhoneFind.Encode(301));
        Assert.Throws<ArgumentOutOfRangeException>(() => PhoneFind.Encode(-1));
        Assert.Null(PhoneFind.Decode(new byte[] { 0x01, 0x2D }));
        Assert.Null(PhoneFind.Decode(new byte[] { 0x3C }));
        Assert.Null(PhoneFind.Decode(Array.Empty<byte>()));
    }
}
