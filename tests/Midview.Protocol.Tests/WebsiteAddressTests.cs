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

    [Theory]
    [InlineData("www.google.com, www.youtube.com")]
    [InlineData("www.google.com,www.youtube.com")]
    [InlineData("www.google.com www.youtube.com")]
    [InlineData("www.google.com; www.youtube.com,")]
    [InlineData("  www.google.com ,  www.youtube.com  ")]
    public void TryParseMany_SeveralSites_GivesEachInOrder(string typed)
    {
        Assert.True(WebsiteAddress.TryParseMany(typed, out var addresses, out var problem));
        Assert.Equal(new[] { "https://www.google.com/", "https://www.youtube.com/" }, addresses.Select(a => a.AbsoluteUri));
        Assert.Equal(string.Empty, problem);
    }

    [Fact]
    public void TryParseMany_OneSite_IsAsBefore()
    {
        Assert.True(WebsiteAddress.TryParseMany("youtube.com/watch?v=abc", out var addresses, out _));
        Assert.Equal("https://youtube.com/watch?v=abc", Assert.Single(addresses).AbsoluteUri);
    }

    // A map link has commas of its own: it stays one address.
    [Fact]
    public void TryParseMany_ACommaInsideAnAddress_StaysInIt()
    {
        Assert.True(WebsiteAddress.TryParseMany(
            "https://www.google.com/maps/@-26.9,-49.0,15z, www.youtube.com", out var addresses, out _));
        Assert.Equal(
            new[] { "https://www.google.com/maps/@-26.9,-49.0,15z", "https://www.youtube.com/" },
            addresses.Select(a => a.AbsoluteUri));
    }

    [Fact]
    public void TryParseMany_TheSameSiteTwice_OpensOnce()
    {
        Assert.True(WebsiteAddress.TryParseMany("google.com, https://google.com", out var addresses, out _));
        Assert.Single(addresses);
    }

    [Fact]
    public void TryParseMany_OneBadAddress_SaysWhich()
    {
        Assert.False(WebsiteAddress.TryParseMany("www.google.com, notepad", out var addresses, out var problem));
        Assert.Empty(addresses);
        Assert.StartsWith("\"notepad\": Isso não parece", problem);
    }

    [Fact]
    public void TryParseMany_AFileAmongTheSites_IsRefused()
    {
        Assert.False(WebsiteAddress.TryParseMany(@"www.google.com, C:\Windows\System32\calc.exe", out _, out var problem));
        Assert.Contains("Só endereços de sites", problem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" , ")]
    public void TryParseMany_Nothing_AsksForAnAddress(string typed)
    {
        Assert.False(WebsiteAddress.TryParseMany(typed, out _, out var problem));
        Assert.NotEqual(string.Empty, problem);
    }

    [Fact]
    public void TryParseMany_TooMany_IsRefused()
    {
        var typed = string.Join(", ", Enumerable.Range(1, WebsiteAddress.MaxSites + 1).Select(i => $"site{i}.com"));

        Assert.False(WebsiteAddress.TryParseMany(typed, out _, out var problem));
        Assert.Equal($"No máximo {WebsiteAddress.MaxSites} sites de uma vez.", problem);
    }
}
