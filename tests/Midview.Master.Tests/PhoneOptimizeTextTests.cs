using Midview.Master;
using Midview.Protocol;
using Xunit;

public class PhoneOptimizeTextTests
{
    private static PhoneOptimizeReport Report(int hidden, string dns, int failed = 0, int uninstalled = 0, int pending = 0) => new(
        DateTimeOffset.UnixEpoch,
        Enumerable.Range(0, hidden).Select(i => $"com.anuncio.{i}").ToList(),
        Enumerable.Range(0, failed).Select(i => $"com.falhou.{i}").ToList(),
        dns, "dns.adguard-dns.com")
    {
        Uninstalled = Enumerable.Range(0, uninstalled).Select(i => $"com.jogo.{i}").ToList(),
        Pending = pending,
    };

    // Apps still leaving the phone in the background (0.0.75 on).
    [Fact]
    public void Apps_still_leaving_are_said() =>
        Assert.Equal("13 apps desinstalados (mais 22 saindo no celular) · 21 escondidos · DNS sem anúncios ligado",
                     PhoneOptimizeText.Line(Report(21, PhoneDnsStates.On, uninstalled: 13, pending: 22)));

    [Theory]
    [InlineData(1, 0, 1, 0, "1 app desinstalado (mais 1 saindo no celular)")]
    [InlineData(0, 6, 3, 0, "nenhum app desinstalado ainda (mais 3 saindo no celular) · 6 escondidos")]
    [InlineData(2, 1, 4, 1, "2 apps desinstalados (mais 4 saindo no celular) · 1 escondido (1 não pôde ser removido)")]
    public void Leaving_variants(int uninstalled, int hidden, int pending, int failed, string apps) =>
        Assert.Equal($"{apps} · DNS sem anúncios ligado",
                     PhoneOptimizeText.Line(Report(hidden, PhoneDnsStates.On, failed, uninstalled, pending)));

    // Done (a check mark), but not "Pronto." while apps are still leaving.
    [Fact]
    public void Still_leaving_is_done_but_not_finished()
    {
        var report = Report(21, PhoneDnsStates.On, uninstalled: 13, pending: 22);
        Assert.True(PhoneOptimizeText.IsDone(report));
        Assert.True(PhoneOptimizeText.IsRemoving(report));
        Assert.False(PhoneOptimizeText.IsRemoving(Report(21, PhoneDnsStates.On, uninstalled: 35)));
        Assert.False(PhoneOptimizeText.IsRemoving(null));
    }

    [Theory]
    [InlineData(1, 1, 1, "Quase pronto: o celular continua desinstalando os apps que faltam.")]
    [InlineData(3, 3, 1, "Quase pronto nos 3 aparelhos: 1 continua desinstalando os apps que faltam.")]
    [InlineData(3, 3, 2, "Quase pronto nos 3 aparelhos: 2 continuam desinstalando os apps que faltam.")]
    [InlineData(2, 3, 1, "Otimizados 2 de 3 aparelhos (veja abaixo). Dos otimizados, 1 continua desinstalando os apps que faltam.")]
    [InlineData(1, 1, 0, "Pronto.")]
    [InlineData(3, 3, 0, "Pronto nos 3 aparelhos.")]
    public void Summary_while_removing(int done, int total, int removing, string expected) =>
        Assert.Equal(expected, PhoneOptimizeText.Summary(done, total, removing));

    [Fact]
    public void Everything_on() =>
        Assert.Equal("28 apps desinstalados · 6 escondidos · DNS sem anúncios ligado",
                     PhoneOptimizeText.Line(Report(6, PhoneDnsStates.On, uninstalled: 28)));

    [Theory]
    [InlineData(PhoneDnsStates.OffNetworkBlocks, "15 apps escondidos · DNS sem anúncios desligado: a rede bloqueou")]
    [InlineData(PhoneDnsStates.OffNoInternet, "15 apps escondidos · DNS sem anúncios desligado: o celular estava sem internet")]
    [InlineData(PhoneDnsStates.OffError, "15 apps escondidos · DNS sem anúncios desligado: o celular não conseguiu ligá-lo")]
    [InlineData(PhoneDnsStates.OffByTeacher, "15 apps escondidos · DNS sem anúncios desligado no Painel do laboratório")]
    public void Dns_off_says_why(string dns, string expected) =>
        Assert.Equal(expected, PhoneOptimizeText.Line(Report(15, dns)));

    // With the internet blocked the phone cannot check the DNS now; it does so
    // by itself when the internet is released, so this is not a failure.
    [Fact]
    public void Internet_blocked_is_checked_again_later() =>
        Assert.Equal("15 apps escondidos · DNS sem anúncios: conferido de novo quando a internet for liberada",
                     PhoneOptimizeText.Line(Report(15, PhoneDnsStates.OffInternetBlocked)));

    [Theory]
    [InlineData(1, 0, "1 app desinstalado")]
    [InlineData(1, 1, "1 app desinstalado · 1 escondido")]
    [InlineData(2, 1, "2 apps desinstalados · 1 escondido")]
    [InlineData(35, 0, "35 apps desinstalados")]
    [InlineData(0, 1, "1 app escondido")]
    [InlineData(0, 6, "6 apps escondidos")]
    public void Uninstalled_and_hidden_are_counted(int uninstalled, int hidden, string apps) =>
        Assert.Equal($"{apps} · DNS sem anúncios ligado", PhoneOptimizeText.Line(Report(hidden, PhoneDnsStates.On, uninstalled: uninstalled)));

    [Fact]
    public void No_app_to_remove() =>
        Assert.Equal("nenhum app desnecessário neste celular · DNS sem anúncios ligado", PhoneOptimizeText.Line(Report(0, PhoneDnsStates.On)));

    [Theory]
    [InlineData(0, 1, "nenhum app desinstalado nem escondido (1 não pôde ser removido)")]
    [InlineData(13, 1, "13 apps escondidos (1 não pôde ser removido)")]
    [InlineData(13, 2, "13 apps escondidos (2 não puderam ser removidos)")]
    public void Apps_that_could_not_be_removed_are_counted(int hidden, int failed, string start) =>
        Assert.StartsWith(start + " · ", PhoneOptimizeText.Line(Report(hidden, PhoneDnsStates.On, failed)));

    // A report from an agent before 0.0.74 has no uninstalled apps.
    [Fact]
    public void Older_agent_report_only_hides()
    {
        Assert.True(PhoneOptimizeReport.TryDecode(
            System.Text.Encoding.UTF8.GetBytes(
                "{\"optimizedAt\":null,\"hidden\":[\"a.b\",\"c.d\"],\"failed\":[],\"dns\":\"on\",\"dnsHost\":\"\"}"),
            out var report));
        Assert.Equal("2 apps escondidos · DNS sem anúncios ligado", PhoneOptimizeText.Line(report));
    }

    [Fact]
    public void Older_agent() =>
        Assert.Equal("o Midview deste celular é de uma versão anterior e ainda não otimiza", PhoneOptimizeText.Line(null));

    [Fact]
    public void Only_a_report_with_the_dns_on_or_waiting_for_the_internet_is_done()
    {
        Assert.True(PhoneOptimizeText.IsDone(Report(3, PhoneDnsStates.On)));
        Assert.True(PhoneOptimizeText.IsDone(Report(3, PhoneDnsStates.OffInternetBlocked)));
        // The teacher's own choice in the lab panel.
        Assert.True(PhoneOptimizeText.IsDone(Report(3, PhoneDnsStates.OffByTeacher)));
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
