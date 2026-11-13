using Midview.Protocol;
using Xunit;

public class PhoneNetworkFreeTests
{
    [Fact]
    public void MessageTypes_AreTheWireNumbers()
    {
        Assert.Equal(103, (int)MessageType.FreePhoneNetwork);
        Assert.Equal(104, (int)MessageType.SetPhoneAdminPassword);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(60)]
    public void ZeroToSixtyMinutes_RoundTrip(int minutes)
    {
        Assert.True(PhoneNetworkFree.IsValid(minutes));
        Assert.Equal(new[] { (byte)minutes }, PhoneNetworkFree.Encode(minutes));
        Assert.Equal(minutes, PhoneNetworkFree.Decode(PhoneNetworkFree.Encode(minutes)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(61)]
    [InlineData(255)]
    public void OutOfRange_IsRefused(int minutes)
    {
        Assert.False(PhoneNetworkFree.IsValid(minutes));
        Assert.Throws<ArgumentOutOfRangeException>(() => PhoneNetworkFree.Encode(minutes));
    }

    [Fact]
    public void Decode_RefusesAnyOtherPayload()
    {
        Assert.Null(PhoneNetworkFree.Decode(Array.Empty<byte>()));
        Assert.Null(PhoneNetworkFree.Decode(new byte[] { 61 }));
        Assert.Null(PhoneNetworkFree.Decode(new byte[] { 0xFF }));
        Assert.Null(PhoneNetworkFree.Decode(new byte[] { 10, 0 }));
    }

    [Fact]
    public void TheChoices_AreTenThirtyAndSixty()
    {
        Assert.Equal(new[] { 10, 30, 60 }, PhoneNetworkFree.Choices);
        Assert.Contains(PhoneNetworkFree.DefaultMinutes, PhoneNetworkFree.Choices);
    }
}
