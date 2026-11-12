using Midview.Master;
using Xunit;

public class TileMovesTests
{
    private static readonly string Session = new('5', 40);

    private static KnownMachine Machine(string name, string host, char agent) => new(name, host, Session, new string(agent, 40));

    // The grid as MainWindow keeps it: replacing a tile gives its screen
    // back (RemoveTileAt), and the last lock to go forgets the phones the
    // class lock left out.
    private sealed class Grid : ITileMoveHost
    {
        public List<KnownMachine> Tiles { get; } = new();
        public HashSet<string> Locked { get; } = new();
        public List<string> Log { get; } = new();

        public string? LockLeftOut { get; set; }

        public int IndexOf(KnownMachine machine) => Tiles.FindIndex(tile => tile.IsSameEntryAs(machine));

        public bool IsLocked(int index) => Locked.Contains(Tiles[index].Name);

        public void Replace(int index, KnownMachine updated)
        {
            Locked.Remove(Tiles[index].Name);
            if (Locked.Count == 0)
            {
                LockLeftOut = null;
            }
            Tiles[index] = updated;
            Log.Add($"replace {updated.Name} {updated.HostAddress}");
        }

        public void Lock(int index)
        {
            Locked.Add(Tiles[index].Name);
            Log.Add($"lock {Tiles[index].Name} {Tiles[index].HostAddress} ({LockLeftOut})");
        }
    }

    [Fact]
    public void ALockedMachine_IsLockedAgainAtItsNewAddress_KeepingTheLeftOutNotice()
    {
        var phone = Machine("Celular 35F7", "192.168.15.13", 'A');
        var grid = new Grid { LockLeftOut = "1 celular ficou de fora." };
        grid.Tiles.Add(phone);
        grid.Locked.Add(phone.Name);

        var moved = TileMoves.Apply(grid, new[] { (phone, phone with { HostAddress = "192.168.0.134" }) });

        Assert.Equal(1, moved);
        Assert.Equal(new[] { "replace Celular 35F7 192.168.0.134", "lock Celular 35F7 192.168.0.134 (1 celular ficou de fora.)" }, grid.Log);
        Assert.Equal("1 celular ficou de fora.", grid.LockLeftOut);
    }

    [Fact]
    public void AnUnlockedMachine_IsNotLocked()
    {
        var pc = Machine("Aluno-02", "10.0.0.2", 'A');
        var grid = new Grid();
        grid.Tiles.Add(pc);

        TileMoves.Apply(grid, new[] { (pc, pc with { HostAddress = "10.0.0.9" }) });

        Assert.Equal(new[] { "replace Aluno-02 10.0.0.9" }, grid.Log);
        Assert.Empty(grid.Locked);
    }

    // The proof takes a few seconds; the teacher may remove or edit the
    // machine meanwhile: then it is left alone.
    [Fact]
    public void AMachineRemovedOrEditedMeanwhile_IsLeftAlone()
    {
        var pc = Machine("Aluno-02", "10.0.0.2", 'A');
        var grid = new Grid();
        grid.Tiles.Add(pc with { Name = "Lab-02" });

        Assert.Equal(0, TileMoves.Apply(grid, new[] { (pc, pc with { HostAddress = "10.0.0.9" }) }));
        Assert.Empty(grid.Log);
    }

    [Fact]
    public void TwoMachinesThatSwappedAddresses_BothMove_EachKeepingItsLock()
    {
        var a = Machine("Aluno-05", "10.0.0.5", 'A');
        var b = Machine("Aluno-06", "10.0.0.6", 'B');
        var grid = new Grid();
        grid.Tiles.AddRange(new[] { a, b });
        grid.Locked.Add(b.Name);

        TileMoves.Apply(grid, new[] { (a, a with { HostAddress = "10.0.0.6" }), (b, b with { HostAddress = "10.0.0.5" }) });

        Assert.Equal(new[] { "10.0.0.6", "10.0.0.5" }, grid.Tiles.Select(tile => tile.HostAddress));
        Assert.Equal(new[] { "Aluno-06" }, grid.Locked);
    }
}
