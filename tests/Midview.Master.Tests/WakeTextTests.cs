using Midview.Master;
using Xunit;

public class WakeTextTests
{
    [Fact]
    public void Summary_SentToEveryone()
    {
        Assert.Equal(
            "Pedido para ligar enviado a 24 máquinas. Elas aparecem na grade em 1 ou 2 minutos.",
            WakeText.Summary(sentTo: Names(24), unknown: Array.Empty<string>(), sent: true, error: null));
    }

    [Fact]
    public void Summary_OneMachine()
    {
        Assert.Equal(
            "Pedido para ligar enviado a ALUNO-03. Ela aparece na grade em 1 ou 2 minutos.",
            WakeText.Summary(sentTo: new[] { "ALUNO-03" }, unknown: Array.Empty<string>(), sent: true, error: null));
    }

    // A machine whose network card the Master hasn't seen yet can't be woken.
    [Fact]
    public void Summary_NamesTheOnesItCantWake()
    {
        Assert.Equal(
            "Pedido para ligar enviado a 22 máquinas. Elas aparecem na grade em 1 ou 2 minutos."
            + " Sem a placa de rede conhecida (ligue-as uma vez com o Midview aberto): ALUNO-07, ALUNO-12.",
            WakeText.Summary(sentTo: Names(22), unknown: new[] { "ALUNO-07", "ALUNO-12" }, sent: true, error: null));
    }

    [Fact]
    public void Summary_ManyItCantWake_NamesTheFirstFive()
    {
        var text = WakeText.Summary(sentTo: Names(2), unknown: Names(8), sent: true, error: null);

        Assert.EndsWith("ALUNO-05 e mais 3.", text);
    }

    [Fact]
    public void Summary_NoneKnown()
    {
        Assert.Equal(
            "Nenhuma dessas máquinas tem a placa de rede conhecida ainda: ligue cada uma uma vez com o Midview aberto.",
            WakeText.Summary(sentTo: Array.Empty<string>(), unknown: Names(3), sent: false, error: null));
    }

    [Fact]
    public void Summary_CouldntSend_SaysWhyAndWhatToCheck()
    {
        var text = WakeText.Summary(sentTo: Names(3), unknown: Array.Empty<string>(), sent: false, error: "rede indisponível");

        Assert.StartsWith("O pedido para ligar não pôde ser enviado (rede indisponível)", text);
        Assert.Contains("na rede", text);
    }

    private static string[] Names(int count) => Enumerable.Range(1, count).Select(n => $"ALUNO-{n:00}").ToArray();
}
