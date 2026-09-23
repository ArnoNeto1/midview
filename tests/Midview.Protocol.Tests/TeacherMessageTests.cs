using Midview.Protocol;
using Xunit;

public class TeacherMessageTests
{
    [Fact]
    public void EncodeThenDecode_RoundTripsAccentedText()
    {
        var text = "Atenção: a prova começa às 10h. Não esqueçam o caderno!";

        Assert.Equal(text, TeacherMessage.Decode(TeacherMessage.Encode(text)));
    }

    [Fact]
    public void Decode_TrimsSurroundingWhitespace()
    {
        Assert.Equal("Olá", TeacherMessage.Decode(TeacherMessage.Encode("  Olá \r\n")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void Decode_NothingToShow_ReturnsNull(string text)
    {
        Assert.Null(TeacherMessage.Decode(TeacherMessage.Encode(text)));
    }

    [Fact]
    public void Decode_LongerThanTheMaximum_ReturnsNull()
    {
        var atLimit = new string('a', TeacherMessage.MaxLength);

        Assert.Equal(atLimit, TeacherMessage.Decode(TeacherMessage.Encode(atLimit)));
        Assert.Null(TeacherMessage.Decode(TeacherMessage.Encode(atLimit + "a")));
    }

    [Fact]
    public void Decode_InvalidUtf8_ReturnsNull()
    {
        Assert.Null(TeacherMessage.Decode(new byte[] { 0x4F, 0xC3, 0x28 }));
    }
}
