using System.Text;
using Midview.Protocol;
using Xunit;

public class PhoneScreenPasswordTests
{
    [Fact]
    public void MessageType_IsTheWireNumber() => Assert.Equal(100, (int)MessageType.SetPhoneScreenPassword);

    [Theory]
    [InlineData("1234")]
    [InlineData("0000")]
    [InlineData("9876")]
    [InlineData("")]
    public void FourDigitsOrNothing_AreValid(string pin)
    {
        Assert.True(PhoneScreenPassword.IsValid(pin));
        Assert.Equal(pin, PhoneScreenPassword.Decode(PhoneScreenPassword.Encode(pin)));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("12a4")]
    [InlineData("abcd")]
    [InlineData("12 4")]
    [InlineData(" 1234")]
    [InlineData("١٢٣٤")] // Arabic-Indic digits: digits, but not ASCII.
    [InlineData("１２３４")] // Fullwidth digits.
    [InlineData(null)]
    public void AnythingElse_IsRefused(string? pin)
    {
        Assert.False(PhoneScreenPassword.IsValid(pin));
        if (pin is not null)
        {
            Assert.Throws<ArgumentException>(() => PhoneScreenPassword.Encode(pin));
            Assert.Null(PhoneScreenPassword.Decode(Encoding.UTF8.GetBytes(pin)));
        }
    }

    [Fact]
    public void Encode_IsTheAsciiDigits()
    {
        // Same bytes as PhoneScreenPasswordTest.kt (the Android agent).
        Assert.Equal(new byte[] { 0x31, 0x32, 0x33, 0x34 }, PhoneScreenPassword.Encode("1234"));
        Assert.Empty(PhoneScreenPassword.Encode(PhoneScreenPassword.None));
    }

    [Fact]
    public void Decode_RefusesNonAsciiBytes()
    {
        Assert.Null(PhoneScreenPassword.Decode(new byte[] { 0x31, 0x32, 0x33, 0xB4 }));
        Assert.Null(PhoneScreenPassword.Decode(new byte[] { 0x31, 0x32, 0x33, 0x00 }));
    }

    [Fact]
    public void Invalid_IsTheTeachersText() => Assert.Equal("o PIN tem que ter 4 números", PhoneScreenPassword.Invalid);
}
