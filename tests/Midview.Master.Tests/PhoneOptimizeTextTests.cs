using Midview.Master;
using Midview.Protocol;
using Xunit;

public class PhoneOptimizeTextTests
{
    private static PhoneOptimizeReport Report(int hidden, string dns, int failed = 0) => new(
        DateTimeOffset.UnixEpoch,
        Enumerable.Range(0, hidden).Select(i => $"com.anuncio.{i}").ToList(),
        Enumerable.Range(0, failed).Select(i => $"com.falhou.{i}").ToList(),
        dns, "dns.adguard-dns.com");

    [Fact]
    public void Everything_on() =>
        Assert.Equal("15 apps de anúncios escondidos · DNS sem anúncios ligado",
                     PhoneOptimizeText.Line(Report(15, PhoneDnsStates.On)));

    [Theory]
    [InlineData(PhoneDnsStates.OffNetworkBlocks, "15 apps de anúncios escondidos · DNS sem anúncios desligado: a rede bloqueou")]
    [InlineData(PhoneDnsStates.OffNoInternet, "15 apps de anúncios escondidos · DNS sem anúncios desligado: o celular estava sem internet")]
    [InlineData(PhoneDnsStates.OffError, "15 apps de anúncios escondidos · DNS sem anúncios desligado: o celular não conseguiu ligá-lo")]
    public void Dns_off_says_why(string dns, string expected) =>
        Assert.Equal(expected, PhoneOptimizeText.Line(Report(15, dns)));

    // With the internet blocked the phone cannot check the DNS now; it does so
    // by itself when the internet is released, so this is not a failure.
    [Fact]
    public void Internet_blocked_is_checked_again_later() =>
        Assert.Equal("15 apps de anúncios escondidos · DNS sem anúncios: conferido de novo quando a internet for liberada",
                     PhoneOptimizeText.Line(Report(15, PhoneDnsStates.OffInternetBlocked)));

    [Fact]
    public void One_app_is_singular() =>
        Assert.Equal("1 app de anúncios escondido · DNS sem anúncios ligado", PhoneOptimizeText.Line(Report(1, PhoneDnsStates.On)));

    [Fact]
    public void No_app_to_hide() =>
        Assert.Equal("nenhum app de anúncios neste celular · DNS sem anúncios ligado", PhoneOptimizeText.Line(Report(0, PhoneDnsStates.On)));

    [Theory]
    [InlineData(1, "13 apps de anúncios escondidos (1 não pôde ser escondido)")]
    [InlineData(2, "13 apps de anúncios escondidos (2 não puderam ser escondidos)")]
    public void Apps_that_could_not_be_hidden_are_counted(int failed, string start) =>
        Assert.StartsWith(start + " · ", PhoneOptimizeText.Line(Report(13, PhoneDnsStates.On, failed)));

    [Fact]
    public void Older_agent() =>
        Assert.Equal("o Midview deste celular é de uma versão anterior e ainda não otimiza", PhoneOptimizeText.Line(null));

    [Fact]
    public void Only_a_report_with_the_dns_on_or_waiting_for_the_internet_is_done()
    {
        Assert.True(PhoneOptimizeText.IsDone(Report(3, PhoneDnsStates.On)));
        Assert.True(PhoneOptimizeText.IsDone(Report(3, PhoneDnsStates.OffInternetBlocked)));
        Assert.False(PhoneOptimizeText.IsDone(Report(3, PhoneDnsStates.OffNetworkBlocks)));
        Assert.False(PhoneOptimizeText.IsDone(Report(3, PhoneDnsStates.OffNoInternet)));
        Assert.False(PhoneOptimizeText.IsDone(Report(3, PhoneDnsStates.On, failed: 1)));
        Assert.False(PhoneOptimizeText.IsDone(Report(3, PhoneDnsStates.OffInternetBlocked, failed: 1)));
        Assert.False(PhoneOptimizeText.IsDone(null));
    }

    [Theory]
    [InlineData(1, "Otimizar o celular ou tablet")]
    [InlineData(3, "Otimizar celulares e tablets")]
    public void Heading(int phones, string expected) => Assert.Equal(expected, PhoneOptimizeText.Heading(phones));

    [Theory]
    [InlineData(1, 1, "Pronto.")]
    [InlineData(0, 1, "O aparelho não foi otimizado (veja abaixo).")]
    [InlineData(3, 3, "Pronto nos 3 aparelhos.")]
    [InlineData(2, 3, "Otimizados 2 de 3 aparelhos (veja abaixo).")]
    [InlineData(1, 3, "Otimizado 1 de 3 aparelhos (veja abaixo).")]
    public void Summary(int done, int total, string expected) => Assert.Equal(expected, PhoneOptimizeText.Summary(done, total));
}
