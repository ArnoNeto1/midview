using System.Net.Sockets;
using Midview.Master;
using Midview.Protocol;
using Xunit;

public class ScheduleTextTests
{
    private static KnownMachine Pc(string name) => new(name, "192.0.2.1", "ANY", "AGENT");

    private static KnownMachine Android(string name) => new(name, "192.0.2.9", "ANY", "AGENT", Kind: DeviceKind.Android);

    [Fact]
    public void A_room_of_computers_counts_machines()
    {
        Assert.Equal("Na turma toda (2 máquinas)", ScheduleText.Targets(new[] { Pc("PC 01"), Pc("PC 02") }));
        Assert.Equal("Na turma toda (1 máquina)", ScheduleText.Targets(new[] { Pc("PC 01") }));
        Assert.Equal("Na turma toda (0 máquinas)", ScheduleText.Targets(Array.Empty<KnownMachine>()));
    }

    [Fact]
    public void Phones_and_tablets_are_counted_by_kind()
    {
        Assert.Equal(
            "Na turma toda (5 máquinas: 2 computadores, 2 celulares e 1 tablet)",
            ScheduleText.Targets(new[] { Pc("PC 01"), Pc("PC 02"), Android("Celular 01"), Android("Celular 02"), Android("Tablet 01") }));
        Assert.Equal(
            "Na turma toda (2 máquinas: 1 computador e 1 celular)",
            ScheduleText.Targets(new[] { Pc("PC 01"), Android("Celular 01") }));
        Assert.Equal("Na turma toda (3 celulares)", ScheduleText.Targets(new[] { Android("Celular 01"), Android("Celular 02"), Android("Celular 03") }));
        Assert.Equal("Na turma toda (2 máquinas: 1 celular e 1 tablet)", ScheduleText.Targets(new[] { Android("Celular 01"), Android("Tablet 01") }));
    }

    [Fact]
    public void The_android_hint_says_what_a_phone_does()
    {
        Assert.Equal("Nos celulares e tablets, \"Desligar\" apaga e trava a tela; ligar não é possível.", ScheduleText.AndroidHint);
    }

    [Fact]
    public void The_wake_line_is_the_wake_tasks_with_computers_in_the_room()
    {
        var room = new[] { Pc("PC 01"), Android("Celular 01") };
        Assert.Equal((true, "✓ Ligar: liga às 07:00 (seg a sex)"), ScheduleText.WakeLine(true, "liga às 07:00 (seg a sex)", true, room));
        Assert.Equal((false, "✗ Ligar: falhou"), ScheduleText.WakeLine(false, "falhou", true, room));
    }

    // Phones alone: a time to turn on is no failure, and says why nothing turns on.
    [Fact]
    public void With_only_phones_a_wake_time_is_not_a_failure()
    {
        var (succeeded, line) = ScheduleText.WakeLine(false, "falhou", true, new[] { Android("Celular 01"), Android("Tablet 01") });
        Assert.True(succeeded);
        Assert.Equal("• Ligar: só para computadores, e esta turma não tem nenhum (celulares e tablets não ligam pelo Midview)", line);
        // Without a time to turn on, the task's own words (it was removed, or there was none).
        Assert.Equal((true, "✓ Ligar: não liga sozinha"), ScheduleText.WakeLine(true, "não liga sozinha", false, new[] { Android("Celular 01") }));
    }

    [Fact]
    public void A_phone_says_what_its_agent_says()
    {
        Assert.Equal(
            (true, "apaga e trava a tela às 18:00 (seg a sex)"),
            ScheduleText.Send(Android("Celular 01"), _ => (true, "apaga e trava a tela às 18:00 (seg a sex)")));
    }

    [Fact]
    public void An_older_phone_agent_is_named_as_one()
    {
        Assert.Equal((false, ScheduleText.OldPhone), ScheduleText.Send(Android("Celular 01"), _ => (false, PhoneAgentCommand.NotOnPhone)));
        Assert.Equal((false, ScheduleText.OldPhone), ScheduleText.Send(Android("Celular 01"), _ => (false, string.Empty)));
        Assert.Equal(
            "o Midview deste celular é de uma versão anterior e ainda não apaga a tela no horário agendado: atualize-o",
            ScheduleText.OldPhone);
    }

    [Fact]
    public void An_older_pc_agent_answers_a_bare_no()
    {
        Assert.Equal(
            (false, "o Midview dessa máquina é de uma versão anterior: reinstale com o pacote atual"),
            ScheduleText.Send(Pc("PC 01"), _ => (false, string.Empty)));
        // A PC Agent's own refusal is passed on.
        Assert.Equal((false, "pedido inválido"), ScheduleText.Send(Pc("PC 01"), _ => (false, "pedido inválido")));
    }

    [Fact]
    public void An_unreachable_machine_gets_it_later()
    {
        var refused = new SocketException((int)SocketError.ConnectionRefused);
        var (phoneOk, phoneDetail) = ScheduleText.Send(Android("Celular 01"), _ => throw refused);
        Assert.False(phoneOk);
        Assert.EndsWith("; ele recebe o agendamento quando o MidviewMaster o vir ligado", phoneDetail);
        var (pcOk, pcDetail) = ScheduleText.Send(Pc("PC 01"), _ => throw refused);
        Assert.False(pcOk);
        Assert.EndsWith("; ela recebe o agendamento quando o MidviewMaster a vir ligada", pcDetail);
    }

    [Fact]
    public void Without_its_identity_nothing_is_sent()
    {
        var sent = false;
        var (ok, detail) = ScheduleText.Send(Android("Celular 01") with { AgentThumbprint = null }, _ => { sent = true; return (true, "ok"); });
        Assert.False(ok);
        Assert.Equal(PhoneAgentCommand.NoIdentity, detail);
        Assert.False(sent);
        Assert.StartsWith("falta o thumbprint do Agent", ScheduleText.Send(Pc("PC 01") with { AgentThumbprint = null }, _ => (true, "ok")).Detail);
    }
}
