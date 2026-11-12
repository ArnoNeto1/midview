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

    // The Master's own search (AddressSearchSchedule) moves only machines that
    // aren't answering: a PC answering at its address stays, even when it
    // answered the search from its other network card.
    [Fact]
    public void TheMastersOwnSearch_MovesOnlyOfflineMachines()
    {
        var roster = new[] { Machine("Aluno-2", "10.0.0.2", X), Machine("Celular 35F7", "192.168.15.13", Y) };
        var found = new[]
        {
            new FoundMachine("Aluno-2", "10.0.0.77", X, "you", false),
            new FoundMachine("Celular 35F7", "192.168.0.134", Y, "you", false, Midview.Protocol.DeviceKind.Android),
        };

        var changes = Roster.NewAddressesWhere(roster, found, index => index == 1);

        Assert.Equal(1, changes.Single().Index);
        Assert.Equal("192.168.0.134", changes.Single().Updated.HostAddress);
        Assert.Equal(roster[1] with { HostAddress = "192.168.0.134" }, changes.Single().Updated);
    }

    // Matched by Agent only, as "Procurar na rede": an answer with the same
    // name but another Agent moves nothing, and nothing found is ever added.
    [Fact]
    public void TheMastersOwnSearch_NeverMatchesByName_NorAddsMachines()
    {
        var roster = new[] { Machine("Aluno-2", "10.0.0.2", X), Machine("Aluno-3", "10.0.0.3", null) };
        var found = new[]
        {
            new FoundMachine("Aluno-2", "10.0.0.9", Y, "you", false),
            new FoundMachine("Aluno-3", "10.0.0.10", new string('C', 40), "you", false),
            new FoundMachine("Aluno-4", "10.0.0.11", new string('D', 40), "none", false),
        };

        Assert.Empty(Roster.NewAddressesWhere(roster, found, _ => true));
    }

    [Fact]
    public void OnlyEntriesKeptByAddress_WithTheirAgent_MoveBySearch()
    {
        Assert.True(Roster.MovesBySearch(Machine("Aluno-2", "10.0.0.2", X)));
        Assert.False(Roster.MovesBySearch(Machine("Aluno-2", "ALUNO-02", X)));
        Assert.False(Roster.MovesBySearch(Machine("Aluno-2", "10.0.0.2", null)));
    }

    // How MainWindow takes a colleague's class: Place, one machine at a time,
    // in the colleague's order, touching only what changes.
    [Fact]
    public void AColleaguesClass_PlacedOneByOne_ReplacesWhatWasThere_AndKeepsClassroomOrder()
    {
        var roster = new List<KnownMachine> { Machine("Aluno-2", "10.0.0.2", X), Machine("Lab-9", "10.0.0.9", null) };
        var given = new[]
        {
            Machine("Aluno-1", "10.0.0.1", new string('C', 40)),
            Machine("Aluno-2", "10.0.0.22", X),
            Machine("Aluno-10", "10.0.0.10", Y),
            Machine("Lab-9", "10.0.0.9", null),
        };
        var touched = new List<string>();

        foreach (var machine in given)
        {
            var placed = Roster.Place(roster, machine);
            if (!Roster.Changes(roster, placed))
            {
                continue;
            }

            touched.Add(machine.Name);
            if (placed.Replaces)
            {
                roster.RemoveAt(placed.Index);
            }
            roster.Insert(placed.Index, placed.Machine);
        }

        Assert.Equal(new[] { "Aluno-1", "Aluno-2", "Aluno-10", "Lab-9" }, roster.Select(machine => machine.Name));
        Assert.Equal("10.0.0.22", roster[1].HostAddress);
        Assert.Equal(new[] { "Aluno-1", "Aluno-2", "Aluno-10" }, touched);
    }

    [Fact]
    public void AMachineTheListHasExactlyAsGiven_IsNotAChange()
    {
        var roster = new[] { Machine("Aluno-2", "10.0.0.2", X, "00-1A-2B-3C-4D-5E"), Machine("Lab-9", "10.0.0.9", null) };

        Assert.False(Roster.Changes(roster, Roster.Place(roster, Machine("Aluno-2", "10.0.0.2", X, "00-1A-2B-3C-4D-5E"))));
        Assert.False(Roster.Changes(roster, Roster.Place(roster, Machine("Lab-9", "10.0.0.9", null))));
    }

    [Fact]
    public void ANetworkCardTheGivenEntryLacks_DoesNotMakeItAChange()
    {
        var roster = new[] { Machine("Aluno-2", "10.0.0.2", X, "00-1A-2B-3C-4D-5E") };

        Assert.False(Roster.Changes(roster, Roster.Place(roster, Machine("Aluno-2", "10.0.0.2", X))));
    }

    [Fact]
    public void ANetworkCardTheListLacks_IsAChange()
    {
        var roster = new[] { Machine("Aluno-2", "10.0.0.2", X) };

        Assert.True(Roster.Changes(roster, Roster.Place(roster, Machine("Aluno-2", "10.0.0.2", X, "00-1A-2B-3C-4D-5E"))));
    }

    [Fact]
    public void AnotherAddress_AnotherSessionThumbprint_AnotherName_OrAnotherAgent_AreChanges()
    {
        var roster = new[] { Machine("Aluno-2", "10.0.0.2", X) };

        Assert.True(Roster.Changes(roster, Roster.Place(roster, Machine("Aluno-2", "10.0.0.22", X))));
        Assert.True(Roster.Changes(roster, Roster.Place(roster, new KnownMachine("Aluno-2", "10.0.0.2", new string('6', 40), X))));
        Assert.True(Roster.Changes(roster, Roster.Place(roster, Machine("Lab-2", "10.0.0.2", X))));
        Assert.True(Roster.Changes(roster, Roster.Place(roster, Machine("Aluno-2", "10.0.0.2", Y))));
    }

    [Fact]
    public void ANewMachine_IsAChange()
    {
        var roster = new[] { Machine("Aluno-2", "10.0.0.2", X) };

        Assert.True(Roster.Changes(roster, Roster.Place(roster, Machine("Aluno-3", "10.0.0.3", Y))));
        Assert.True(Roster.Changes(Array.Empty<KnownMachine>(), Roster.Place(Array.Empty<KnownMachine>(), Machine("Aluno-3", "10.0.0.3", Y))));
    }
}
