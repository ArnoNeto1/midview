using Midview.Protocol;
using Xunit;

public class WebsiteAddressTests
{
    [Theory]
    [InlineData("www.google.com", "https://www.google.com/")]
    [InlineData("  google.com  ", "https://google.com/")]
    [InlineData("youtube.com/watch?v=abc", "https://youtube.com/watch?v=abc")]
    [InlineData("HTTPS://Example.com/a?b=1#c", "https://example.com/a?b=1#c")]
    [InlineData("http://intranet.escola/notas", "http://intranet.escola/notas")]
    [InlineData("http://intranet", "http://intranet/")]
    [InlineData("localhost:3000", "https://localhost:3000/")]
    [InlineData("google.com:8080/prova", "https://google.com:8080/prova")]
    [InlineData("192.168.0.10/prova", "https://192.168.0.10/prova")]
    public void TryParse_AWebsite_GivesItsFullAddress(string typed, string expected)
    {
        Assert.True(WebsiteAddress.TryParse(typed, out var address, out var problem));
        Assert.Equal(expected, address!.AbsoluteUri);
        Assert.Equal(string.Empty, problem);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData(@"C:\Windows\System32\calc.exe")]
    [InlineData("C:/Windows/System32/calc.exe")]
    [InlineData(@"\\servidor\pasta")]
    [InlineData("//servidor/pasta")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:prof@escola.br")]
    [InlineData("ftp://arquivos.escola.br")]
    [InlineData("ms-settings:display")]
    public void TryParse_AnythingButAWebsite_IsRefused(string typed)
    {
        Assert.False(WebsiteAddress.TryParse(typed, out var address, out var problem));
        Assert.Null(address);
        Assert.StartsWith("Só endereços de sites", problem);
    }

    [Theory]
    [InlineData("google")]
    [InlineData("notepad")]
    [InlineData("www.goo gle.com")]
    [InlineData("https://")]
    public void TryParse_NotAnAddress_SaysHowToWriteOne(string typed)
    {
        Assert.False(WebsiteAddress.TryParse(typed, out var address, out var problem));
        Assert.Null(address);
        Assert.Contains("www.google.com", problem);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_Nothing_AsksForTheAddress(string? typed)
    {
        Assert.False(WebsiteAddress.TryParse(typed, out _, out var problem));
        Assert.StartsWith("Escreva o endereço", problem);
    }

    [Fact]
    public void TryParse_TooLong_IsRefused()
    {
        var typed = "escola.br/" + new string('x', WebsiteAddress.MaxLength);

        Assert.False(WebsiteAddress.TryParse(typed, out _, out var problem));
        Assert.Equal("Endereço longo demais.", problem);
    }
}
