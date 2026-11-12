using System.Net.Sockets;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// "Fechar programas" with phones: the window's words, and what each phone's answer becomes.
public class CloseAppsTargetsTests
{
    private static KnownMachine Pc(string name) => new(name, "192.0.2.1", "ANY", "AGENT");

    private static KnownMachine Phone(string name, string? agent = "AB12") =>
        new(name, "192.0.2.9", "ANY", agent, Kind: DeviceKind.Android);

    private static readonly KnownMachine[] Pcs = { Pc("ALUNO-01"), Pc("ALUNO-02") };
    private static readonly KnownMachine[] Phones = { Phone("Celular 01"), Phone("Celular 02") };
    private static readonly KnownMachine[] Mixed = { Pc("ALUNO-01"), Phone("Celular 01") };

    [Fact]
    public void Pcs_alone_keep_the_window_as_it_was()
    {
        Assert.False(CloseAppsTargets.OnlyPhones(Pcs));
        Assert.Equal(CloseAppsTargets.PcHint, CloseAppsTargets.Hint(Pcs));
        Assert.Null(CloseAppsTargets.Note(Pcs));
        Assert.Equal("Fechar programas", CloseAppsTargets.Button(Pcs));
        Assert.Equal("Avisando os alunos...", CloseAppsTargets.Progress(60));
        Assert.Equal("Fechando...", CloseAppsTargets.Progress(0));
    }

    // Phones alone: the student is warned for the time picked, then all the apps close.
    [Fact]
    public void Phones_alone_are_warned_and_then_closed()
    {
        Assert.True(CloseAppsTargets.OnlyPhones(Phones));
        Assert.Equal(CloseAppsTargets.PhoneHint, CloseAppsTargets.Hint(Phones));
        Assert.Null(CloseAppsTargets.Note(Phones));
        Assert.Equal("Fechar apps", CloseAppsTargets.Button(Phones));
        Assert.Contains("Avisa o aluno", CloseAppsTargets.PhoneHint);
    }

    // PCs and phones: the PCs' choices, and a line saying asking or forcing is for the PCs.
    [Fact]
    public void Phones_with_pcs_are_told_apart()
    {
        Assert.False(CloseAppsTargets.OnlyPhones(Mixed));
        Assert.Equal(CloseAppsTargets.PcHint, CloseAppsTargets.Hint(Mixed));
        Assert.Equal(CloseAppsTargets.PhoneNote, CloseAppsTargets.Note(Mixed));
        Assert.Equal("Fechar programas", CloseAppsTargets.Button(Mixed));
        Assert.Contains("o aluno também é avisado", CloseAppsTargets.PhoneNote);
    }

    [Fact]
    public void Phones_alone_are_named_as_phones()
    {
        Assert.Equal("Em 2 celulares: Celular 01, Celular 02", CloseAppsTargets.Describe(Phones, wholeClass: false));
        Assert.Equal("Em 2 máquinas: ALUNO-01, Celular 01", CloseAppsTargets.Describe(Mixed, wholeClass: false));
        Assert.Equal("Em: Celular 01", CloseAppsTargets.Describe(new[] { Phone("Celular 01") }, wholeClass: false));
        Assert.Equal("Concluído nos 3 celulares.", CloseAppsTargets.Done(3, 0, onlyPhones: true));
        Assert.Equal("Concluído em 2 de 3.", CloseAppsTargets.Done(3, 1, onlyPhones: true));
    }

    [Fact]
    public void Nothing_is_not_phones_alone() => Assert.False(CloseAppsTargets.OnlyPhones(Array.Empty<KnownMachine>()));

    [Theory]
    [InlineData(1, 0, "Concluído.")]
    [InlineData(3, 0, "Concluído nas 3 máquinas.")]
    [InlineData(3, 1, "Concluído em 2 de 3.")]
    public void The_summary_counts_the_machines(int total, int failures, string expected) =>
        Assert.Equal(expected, CloseAppsTargets.Done(total, failures));

    [Fact]
    public void A_phone_goes_to_its_agent_and_its_answer_is_shown_as_it_comes()
    {
        string? target = null;
        var result = CloseAppsTargets.SendToPhone(Phone("Celular 01"), thumbprint =>
        {
            target = thumbprint;
            return (true, "apps fechados; tela inicial na frente");
        });
        Assert.Equal("AB12", target);
        Assert.Equal((true, "apps fechados; tela inicial na frente"), result);
    }

    [Fact]
    public void A_phone_without_its_identity_is_not_asked()
    {
        var asked = false;
        var result = CloseAppsTargets.SendToPhone(Phone("Celular 01", agent: null), _ => { asked = true; return (true, ""); });
        Assert.False(asked);
        Assert.Equal((false, CloseAppsTargets.NoIdentity), result);
    }

    // An agent from before this answers the "not on a phone" refusal (or nothing).
    [Theory]
    [InlineData("Ainda não funciona em celular.")]
    [InlineData("")]
    public void An_older_phone_agent_is_named_as_such(string answer) =>
        Assert.Equal((false, CloseAppsTargets.OldAgent), CloseAppsTargets.SendToPhone(Phone("Celular 01"), _ => (false, answer)));

    [Fact]
    public void A_phone_refusal_keeps_the_agents_words() =>
        Assert.Equal((false, "este celular não fecha apps"),
                     CloseAppsTargets.SendToPhone(Phone("Celular 01"), _ => (false, "este celular não fecha apps")));

    [Fact]
    public void A_phone_that_does_not_answer_is_offline() =>
        Assert.Equal((false, FailureText.Offline),
                     CloseAppsTargets.SendToPhone(Phone("Celular 01"), _ => throw new SocketException((int)SocketError.TimedOut)));
}
