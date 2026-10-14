using Midview.Master;
using Xunit;

public class RosterTests
{
    private static readonly string X = new('A', 40);
    private static readonly string Y = new('B', 40);
    private static readonly string Session = new('5', 40);

    private static KnownMachine Machine(string name, string host, string? agent, string? mac = null) =>
        new(name, host, Session, agent, mac);

    private static (int, bool) Pick((int Index, bool Replaces, KnownMachine Machine) placed) => (placed.Index, placed.Replaces);

    [Fact]
    public void ANewMachine_GoesInClassroomOrder()
    {
        var roster = new[] { Machine("Aluno-2", "10.0.0.2", X), Machine("Aluno-10", "10.0.0.10", Y) };

        Assert.Equal((1, false), Pick(Roster.Place(roster, Machine("Aluno-3", "10.0.0.3", new string('C', 40)))));
        Assert.Equal((2, false), Pick(Roster.Place(roster, Machine("Aluno-11", "10.0.0.11", new string('C', 40)))));
        Assert.Equal((0, false), Pick(Roster.Place(roster, Machine("Aluno-1", "10.0.0.1", new string('C', 40)))));
    }

    [Fact]
    public void TheSameAgent_ReplacesItsEntry_EvenUnderAnotherName()
    {
        var roster = new[] { Machine("Aluno-2", "10.0.0.2", X), Machine("Aluno-10", "10.0.0.10", Y) };

        Assert.Equal((1, true), Pick(Roster.Place(roster, Machine("Lab-10", "10.0.0.50", Y.ToLowerInvariant()))));
    }

    [Fact]
    public void TheSameName_ReplacesIt()
    {
        var roster = new[] { Machine("Aluno-2", "10.0.0.2", X) };

        Assert.Equal((0, true), Pick(Roster.Place(roster, Machine("aluno-2", "10.0.0.2", Y))));
    }

    [Fact]
    public void TheNetworkCardLearnedBefore_IsKept()
    {
        var roster = new[] { Machine("Aluno-2", "10.0.0.2", X, "00-1A-2B-3C-4D-5E") };

        var placed = Roster.Place(roster, Machine("Aluno-2", "10.0.0.2", X));

        Assert.Equal("00-1A-2B-3C-4D-5E", placed.Machine.MacAddress);
    }

    [Fact]
    public void AnEntryKeptByAddress_MovesToWhereTheMachineAnswered()
    {
        var roster = new[] { Machine("Aluno-2", "10.0.0.2", X), Machine("Aluno-3", "10.0.0.3", Y) };
        var found = new[] { new FoundMachine("Aluno-2", "10.0.0.9", X.ToLowerInvariant(), "you", false) };

        var changes = Roster.NewAddresses(roster, found);

        Assert.Equal(0, changes.Single().Index);
        Assert.Equal("10.0.0.9", changes.Single().Updated.HostAddress);
    }

    [Fact]
    public void AnEntryKeptByName_StaysAsTheTeacherTypedIt()
    {
        var roster = new[] { Machine("Aluno-2", "ALUNO-02", X) };
        var found = new[] { new FoundMachine("Aluno-2", "10.0.0.9", X, "you", false) };

        Assert.Empty(Roster.NewAddresses(roster, found));
    }

    [Fact]
    public void NothingMoved_NothingToChange()
    {
        var roster = new[] { Machine("Aluno-2", "10.0.0.2", X), Machine("Aluno-3", "10.0.0.3", null) };
        var found = new[]
        {
            new FoundMachine("Aluno-2", "10.0.0.2", X, "you", false),
            new FoundMachine("Aluno-9", "10.0.0.99", new string('C', 40), "none", false),
        };

        Assert.Empty(Roster.NewAddresses(roster, found));
    }

    // How MainWindow takes a colleague's class: Place, one machine at a time,
    // in the colleague's order.
    [Fact]
    public void AColleaguesClass_PlacedOneByOne_ReplacesWhatWasThere_AndKeepsClassroomOrder()
    {
        var roster = new List<KnownMachine> { Machine("Aluno-2", "10.0.0.2", X), Machine("Lab-9", "10.0.0.9", null) };
        var given = new[]
        {
            Machine("Aluno-1", "10.0.0.1", new string('C', 40)),
            Machine("Aluno-2", "10.0.0.22", X),
            Machine("Aluno-10", "10.0.0.10", Y),
        };

        foreach (var machine in given)
        {
            var (index, replaces, placed) = Roster.Place(roster, machine);
            if (replaces)
            {
                roster.RemoveAt(index);
            }
            roster.Insert(index, placed);
        }

        Assert.Equal(new[] { "Aluno-1", "Aluno-2", "Aluno-10", "Lab-9" }, roster.Select(machine => machine.Name));
        Assert.Equal("10.0.0.22", roster[1].HostAddress);
    }
}
