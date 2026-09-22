using Midview.Agent;
using Xunit;

public class DomainNormalizerTests
{
    [Theory]
    [InlineData("youtube.com", "youtube.com")]
    [InlineData("YouTube.com", "youtube.com")]
    [InlineData(" youtube.com ", "youtube.com")]
    [InlineData("https://youtube.com/watch?v=x", "youtube.com")]
    [InlineData("http://youtube.com", "youtube.com")]
    [InlineData("youtube.com:8080", "youtube.com")]
    [InlineData("youtube.com/path", "youtube.com")]
    public void Normalize_ValidInput_ReturnsCleanLowercaseHostname(string input, string expected)
    {
        Assert.Equal(expected, DomainNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("youtube.com\nSome-Injected-Line evil.example")]
    [InlineData("not a domain")]
    public void Normalize_InvalidInput_ReturnsNull(string input)
    {
        Assert.Null(DomainNormalizer.Normalize(input));
    }
}
