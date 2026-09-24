using Midview.Protocol;
using Xunit;

public class TextMessageTests
{
    [Fact]
    public void EncodeThenDecode_RoundTripsAccentedText()
    {
        var text = "Atenção: a prova começa às 10h. Não esqueçam o caderno!";

        Assert.Equal(text, TextMessage.Decode(TextMessage.Encode(text)));
    }

    [Fact]
    public void Decode_TrimsSurroundingWhitespace()
    {
        Assert.Equal("Olá", TextMessage.Decode(TextMessage.Encode("  Olá \r\n")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void Decode_NothingToShow_ReturnsNull(string text)
    {
        Assert.Null(TextMessage.Decode(TextMessage.Encode(text)));
    }

    [Fact]
    public void Decode_LongerThanTheMaximum_ReturnsNull()
    {
        var atLimit = new string('a', TextMessage.MaxLength);

        Assert.Equal(atLimit, TextMessage.Decode(TextMessage.Encode(atLimit)));
        Assert.Null(TextMessage.Decode(TextMessage.Encode(atLimit + "a")));
    }

    [Fact]
    public void Decode_InvalidUtf8_ReturnsNull()
    {
        Assert.Null(TextMessage.Decode(new byte[] { 0x4F, 0xC3, 0x28 }));
    }
}
