using Midview.Master;
using Xunit;

// "Esconder da sala": which hidden machines stay away from the grid, and
// what the status bar says about them.
public class HiddenMachinesTests
{
    private static readonly KnownMachine Note = new("NOTE-PROF", "10.0.0.9", "S9", "A9", Hidden: true);

    [Fact]
    public void A_hidden_machine_stays_away_while_no_tile_is_it()
    {
        var shown = new[] { new KnownMachine("Aluno-1", "10.0.0.1", "S1", "A1") };

        Assert.Equal(new[] { Note }, HiddenMachines.StillAway(new[] { Note }, shown));
    }

    // Paired again from "Procurar na rede" (same agent), or saved by hand
    // under the same name: it is the tile now, not hidden twice.
    [Theory]
    [InlineData("NOTE-PROF", "OUTRO")]
    [InlineData("note-prof", "OUTRO")]
    [InlineData("Notebook novo", "a9")]
    public void One_back_as_a_tile_is_no_longer_hidden(string name, string agent)
    {
        var shown = new[] { new KnownMachine(name, "10.0.0.20", "S20", agent) };

        Assert.Empty(HiddenMachines.StillAway(new[] { Note }, shown));
    }

    [Fact]
    public void A_hidden_machine_without_an_agent_is_matched_by_name_only()
    {
        var noAgent = Note with { AgentThumbprint = null };
        var shown = new[] { new KnownMachine("Aluno-1", "10.0.0.1", "S1", null) };

        Assert.Equal(new[] { noAgent }, HiddenMachines.StillAway(new[] { noAgent }, shown));
    }

    [Fact]
    public void The_status_bar_reminds_of_the_hidden_ones()
    {
        Assert.Null(HiddenMachinesText.Reminder(0));
        Assert.Equal("1 máquina escondida (Telas > Escondidas).", HiddenMachinesText.Reminder(1));
        Assert.Equal("2 máquinas escondidas (Telas > Escondidas).", HiddenMachinesText.Reminder(2));
    }

    [Fact]
    public void Hiding_and_showing_say_where_the_machine_went()
    {
        Assert.Equal("NOTE-PROF escondida: saiu da grade e dos botões. Para voltar: Telas > Escondidas.",
            HiddenMachinesText.Hidden(new[] { "NOTE-PROF" }));
        Assert.Equal("2 máquinas escondidas: saíram da grade e dos botões. Para voltar: Telas > Escondidas.",
            HiddenMachinesText.Hidden(new[] { "A", "B" }));
        Assert.Equal("NOTE-PROF voltou para a grade.", HiddenMachinesText.Shown(new[] { "NOTE-PROF" }));
        Assert.Equal("2 máquinas voltaram para a grade.", HiddenMachinesText.Shown(new[] { "A", "B" }));
    }
}
