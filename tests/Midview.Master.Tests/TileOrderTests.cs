using Midview.Master;
using Midview.Protocol;
using Xunit;

public class TileOrderTests
{
    private static MachineTile Pc(string name) => new(new KnownMachine(name, "192.0.2.1", "ANY", null));

    private static MachineTile Android(string name) =>
        new(new KnownMachine(name, "192.0.2.1", "ANY", null) with { Kind = DeviceKind.Android });

    // A machine paired later (or renamed) still shows in its place: by
    // name, numbers counted as numbers; the computers first, then the
    // phones, then the tablets.
    [Fact]
    public void TheGrid_GoesByName_ComputersThenPhonesThenTablets()
    {
        var tiles = new List<MachineTile>
        {
            Pc("ALUNO-07"), Pc("ALUNO-25"), Android("Tablet 04"), Pc("ALUNO-10"), Android("Celular 02"),
            Android("Tablet 01"), Pc("ALUNO-08"), Pc("PROFESSOR"),
        };

        tiles.Sort((a, b) => TileOrder.Instance.Compare(a, b));

        Assert.Equal(
            new[] { "ALUNO-07", "ALUNO-08", "ALUNO-10", "ALUNO-25", "PROFESSOR", "Celular 02", "Tablet 01", "Tablet 04" },
            tiles.Select(tile => tile.Name));
    }
}
