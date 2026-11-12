using System.Net.Sockets;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// "Desligar" and "Reiniciar" with phones: the window's words, what each
// machine is sent, and what its answer becomes.
public class PowerTargetsTests
{
    private static KnownMachine Pc(string name, string? agent = "AGENT") => new(name, "192.0.2.1", "ANY", agent);

    private static KnownMachine Phone(string name, string? agent = "AB12") =>
        new(name, "192.0.2.9", "ANY", agent, Kind: DeviceKind.Android);

    private static readonly KnownMachine[] Pcs = { Pc("ALUNO-01"), Pc("ALUNO-02") };
    private static readonly KnownMachine[] Phones = { Phone("Celular 01"), Phone("Celular 02") };
    private static readonly KnownMachine[] Mixed = { Pc("ALUNO-01"), Phone("Celular 01") };

    [Fact]
    public void Pcs_alone_keep_the_window_as_it_was()
    {
        Assert.False(PowerTargets.OnlyPhones(Pcs));
        Assert.Equal(PowerTargets.PcDelayHint, PowerTargets.DelayHint(Pcs));
        Assert.Null(PowerTargets.Note(Pcs));
        Assert.Equal("Em 2 máquinas: ALUNO-01, ALUNO-02", PowerTargets.Describe(Pcs, wholeClass: false));
        Assert.Equal("Concluído nas 2 máquinas.", PowerTargets.Done(Pcs, 0));
    }

    // Phones alone: what "Desligar" does on them is said, and no Windows updates.
    [Fact]
    public void Phones_alone_are_told_what_desligar_does()
    {
        Assert.True(PowerTargets.OnlyPhones(Phones));
        Assert.Equal(PowerTargets.PhoneDelayHint, PowerTargets.DelayHint(Phones));
        Assert.Equal(PowerTargets.PhoneNote, PowerTargets.Note(Phones));
        Assert.Contains("apaga a tela e trava", PowerTargets.PhoneNote);
        Assert.Contains("continua ligado", PowerTargets.PhoneNote);
        Assert.Equal("Em 2 celulares: Celular 01, Celular 02", PowerTargets.Describe(Phones, wholeClass: false));
        Assert.Equal("Concluído nos 2 celulares.", PowerTargets.Done(Phones, 0));
        Assert.Equal("Concluído em 1 de 2.", PowerTargets.Done(Phones, 1));
    }

    [Fact]
    public void Phones_with_pcs_get_the_pcs_window_and_a_line_of_their_own()
    {
        Assert.False(PowerTargets.OnlyPhones(Mixed));
        Assert.Equal(PowerTargets.PcDelayHint, PowerTargets.DelayHint(Mixed));
        Assert.Equal(PowerTargets.MixedPhoneNote, PowerTargets.Note(Mixed));
        Assert.Contains("apaga a tela e trava", PowerTargets.MixedPhoneNote);
        Assert.Contains("só para os computadores", PowerTargets.MixedPhoneNote);
    }

    // The Windows updates go to the PCs only, and never with a cancel.
    [Fact]
    public void A_phone_is_never_sent_the_windows_updates()
    {
        Assert.Equal(PowerCommand.Encode(PowerAction.Restart, 60, installUpdates: true),
                     PowerTargets.Payload(Pc("ALUNO-01"), PowerAction.Restart, 60, installUpdates: true));
        Assert.Equal(PowerCommand.Encode(PowerAction.Restart, 60),
                     PowerTargets.Payload(Phone("Celular 01"), PowerAction.Restart, 60, installUpdates: true));
        Assert.Equal(PowerCommand.Encode(PowerAction.Cancel, 0),
                     PowerTargets.Payload(Pc("ALUNO-01"), PowerAction.Cancel, 0, installUpdates: true));
    }

    [Fact]
    public void A_phone_goes_to_its_agent_and_its_answer_is_shown_as_it_comes()
    {
        string? target = null;
        byte[]? sent = null;
        var result = PowerTargets.Send(Phone("Celular 01"), PowerAction.Shutdown, 60, installUpdates: true, (thumbprint, payload) =>
        {
            target = thumbprint;
            sent = payload;
            return (true, "apaga a tela em 1 minuto (o celular continua ligado)");
        });
        Assert.Equal("AB12", target);
        Assert.Equal(PowerCommand.Encode(PowerAction.Shutdown, 60), sent);
        Assert.Equal((true, "apaga a tela em 1 minuto (o celular continua ligado)"), result);
    }

    [Fact]
    public void A_phone_without_its_identity_is_not_asked()
    {
        var asked = false;
        var result = PowerTargets.Send(Phone("Celular 01", agent: null), PowerAction.Restart, 0, false, (_, _) => { asked = true; return (true, ""); });
        Assert.False(asked);
        Assert.Equal((false, PhoneAgentCommand.NoIdentity), result);
    }

    // An agent from before this answers the "not on a phone" refusal (or nothing).
    [Theory]
    [InlineData("Ainda não funciona em celular.")]
    [InlineData("")]
    public void An_older_phone_agent_is_named_as_such(string answer)
    {
        Assert.Equal((false, PowerTargets.OldAgent),
                     PowerTargets.Send(Phone("Celular 01"), PowerAction.Restart, 60, false, (_, _) => (false, answer)));
        Assert.Equal("o Midview deste celular é de uma versão anterior e ainda não reinicia nem apaga a tela: atualize-o", PowerTargets.OldAgent);
    }

    [Fact]
    public void A_phone_refusal_keeps_the_agents_words() =>
        Assert.Equal((false, "este celular não reinicia nem apaga a tela"),
                     PowerTargets.Send(Phone("Celular 01"), PowerAction.Restart, 60, false, (_, _) => (false, "este celular não reinicia nem apaga a tela")));

    [Fact]
    public void A_phone_that_does_not_answer_is_offline() =>
        Assert.Equal((false, FailureText.Offline),
                     PowerTargets.Send(Phone("Celular 01"), PowerAction.Restart, 60, false, (_, _) => throw new SocketException((int)SocketError.TimedOut)));

    [Fact]
    public void A_pc_goes_to_its_agent_as_before()
    {
        string? target = null;
        byte[]? sent = null;
        var result = PowerTargets.Send(Pc("ALUNO-01"), PowerAction.Restart, 300, installUpdates: true, (thumbprint, payload) =>
        {
            target = thumbprint;
            sent = payload;
            return (true, "reinicia em 5 minutos, instalando as atualizações");
        });
        Assert.Equal("AGENT", target);
        Assert.Equal(PowerCommand.Encode(PowerAction.Restart, 300, installUpdates: true), sent);
        Assert.Equal((true, "reinicia em 5 minutos, instalando as atualizações"), result);
    }

    [Fact]
    public void A_pc_without_its_agent_thumbprint_is_not_asked() =>
        Assert.Equal((false, PowerTargets.NoAgentThumbprint),
                     PowerTargets.Send(Pc("ALUNO-01", agent: null), PowerAction.Shutdown, 60, false, (_, _) => throw new InvalidOperationException()));

    // An Agent from before the updates refuses the request as invalid (PowerText).
    [Fact]
    public void An_older_pc_agent_asked_for_the_updates_is_named_as_such()
    {
        Assert.Equal(PowerText.Detail(false, "pedido inválido", true),
                     PowerTargets.Send(Pc("ALUNO-01"), PowerAction.Shutdown, 60, true, (_, _) => (false, "pedido inválido")).Detail);
        Assert.Equal("pedido inválido",
                     PowerTargets.Send(Pc("ALUNO-01"), PowerAction.Shutdown, 60, false, (_, _) => (false, "pedido inválido")).Detail);
    }

    [Fact]
    public void A_pc_that_does_not_answer_is_described_as_before()
    {
        var error = new SocketException((int)SocketError.TimedOut);
        Assert.Equal((false, FailureText.DescribeAgent(error)),
                     PowerTargets.Send(Pc("ALUNO-01"), PowerAction.Shutdown, 60, false, (_, _) => throw error));
    }
}
