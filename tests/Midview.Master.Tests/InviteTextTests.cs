using Midview.Master;
using Midview.Protocol;
using Xunit;

public class InviteTextTests
{
    private static readonly string Thumbprint = new('A', 40);

    private static InvitedMachine Machine(string name) => new(name, "10.0.0.1", Thumbprint, Thumbprint, null);

    private static readonly InviteResult SomeLeftOut = new(
        new[] { Machine("ALUNO-01"), Machine("ALUNO-02") },
        new[] { new LeftOutMachine("ALUNO-07", "Offline"), new LeftOutMachine("ALUNO-12", "Midview antigo: atualize-a") });

    [Fact]
    public void Received_SaysHowManyCame_AndWhyTheOthersDidNot()
    {
        Assert.Equal(
            "Pronto: 2 de 4 máquinas na sua sala. Não liberadas: ALUNO-07 (Offline); ALUNO-12 (Midview antigo: atualize-a).",
            InviteText.Received(SomeLeftOut));
        Assert.Equal(
            "Pronto: 1 de 1 máquinas na sua sala.",
            InviteText.Received(new InviteResult(new[] { Machine("ALUNO-01") }, Array.Empty<LeftOutMachine>())));
        Assert.Equal("A sala do colega não tem máquinas.", InviteText.Received(InviteResult.Empty));
    }

    [Fact]
    public void Given_SaysWhoGotTheClass()
    {
        Assert.Equal(
            "PROF-02 recebeu acesso a 2 de 4 máquinas. Não liberadas: ALUNO-07 (Offline); ALUNO-12 (Midview antigo: atualize-a).",
            InviteText.Given("PROF-02", SomeLeftOut));
        Assert.Equal("PROF-02 entrou, mas a sua lista da sala está vazia.", InviteText.Given("PROF-02", InviteResult.Empty));
    }

    [Fact]
    public void Ended_SaysHowTheInviteEnded()
    {
        var one = new InviteResult(new[] { Machine("ALUNO-01") }, Array.Empty<LeftOutMachine>());

        Assert.Equal("PROF-02 recebeu acesso a 1 de 1 máquinas.", InviteText.Ended(new InviteOutcome(InviteEnd.Given, "PROF-02", one, ListSent: true)));
        Assert.Equal(
            "PROF-02 recebeu acesso a 1 de 1 máquinas. A conexão caiu antes de a lista chegar ao colega: gere outro código para ele (as máquinas não se repetem).",
            InviteText.Ended(new InviteOutcome(InviteEnd.Given, "PROF-02", one, ListSent: false)));
        Assert.Equal(
            "Alguém na rede tentou entrar com um código errado. O convite foi cancelado. Gere outro se ainda precisar.",
            InviteText.Ended(new InviteOutcome(InviteEnd.WrongCode)));
        Assert.Equal("O código expirou. Gere outro se ainda precisar.", InviteText.Ended(new InviteOutcome(InviteEnd.Expired)));
        Assert.Equal("O convite foi cancelado.", InviteText.Ended(new InviteOutcome(InviteEnd.Cancelled)));
    }

    [Fact]
    public void Waiting_SaysUntilWhen_AndHowManyMachines()
    {
        var at = new DateTime(2026, 10, 14, 14, 32, 0);

        Assert.Equal(
            "Vale por 10 minutos, uma vez (até 14:32). Deixe esta janela aberta até o colega entrar. Vai liberar as 25 máquinas da sua sala.",
            InviteText.Waiting(25, at));
        Assert.Equal(
            "Vale por 10 minutos, uma vez (até 14:32). Deixe esta janela aberta até o colega entrar. Vai liberar a máquina da sua sala.",
            InviteText.Waiting(1, at));
    }
}
