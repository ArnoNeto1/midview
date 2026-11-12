using Midview.Master;
using Midview.Protocol;
using Xunit;

// Every toolbar button reaches what the teacher sees (DeviceSupport.ToolbarReach):
// the selection when there is one, otherwise every machine the "Telas" filter
// shows. The toolbar's "Bloquear telas" locks or gives back only those
// (ScreenLockReach), and switching the filter never unlocks anything.
public class ToolbarReachTests
{
    private static readonly KnownMachine Pc1 = new("PC 01", "192.0.2.1", "ANY", null);
    private static readonly KnownMachine Cel1 = new("Cel 01", "192.0.2.9", "ANY", null, Kind: DeviceKind.Android);
    private static readonly KnownMachine Pc2 = new("PC 02", "192.0.2.2", "ANY", null);
    private static readonly KnownMachine Cel2 = new("Cel 02", "192.0.2.10", "ANY", null, Kind: DeviceKind.Android);

    // In the grid's order, PCs and phones mixed.
    private static readonly KnownMachine[] Room = { Pc1, Cel1, Pc2, Cel2 };

    private static readonly KnownMachine[] None = Array.Empty<KnownMachine>();

    // Every toolbar action but the ones made for phones.
    public static TheoryData<ToolbarAction> ToolbarActions()
    {
        var data = new TheoryData<ToolbarAction>();
        foreach (var action in Enum.GetValues<ToolbarAction>()
                     .Where(action => !DeviceSupport.IsPhoneOnly(action) && action is not (ToolbarAction.WipePhone or ToolbarAction.UpdatePhones)))
        {
            data.Add(action);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ToolbarActions))]
    public void Nothing_selected_reaches_every_machine_the_filter_shows(ToolbarAction action)
    {
        Assert.Equal(Room, DeviceSupport.ToolbarReach(action, None, Room, DeviceFilter.All));
        Assert.Equal(new[] { Pc1, Pc2 }, DeviceSupport.ToolbarReach(action, None, Room, DeviceFilter.Computers));
        Assert.Equal(new[] { Cel1, Cel2 }, DeviceSupport.ToolbarReach(action, None, Room, DeviceFilter.Phones));
    }

    // The selection only ever holds tiles the grid shows; it is the reach under every filter.
    [Theory]
    [MemberData(nameof(ToolbarActions))]
    public void A_selection_is_the_reach_under_every_filter(ToolbarAction action)
    {
        Assert.Equal(new[] { Cel1, Pc2 }, DeviceSupport.ToolbarReach(action, new[] { Cel1, Pc2 }, Room, DeviceFilter.All));
        Assert.Equal(new[] { Pc1 }, DeviceSupport.ToolbarReach(action, new[] { Pc1 }, Room, DeviceFilter.All));
        Assert.Equal(new[] { Pc2 }, DeviceSupport.ToolbarReach(action, new[] { Pc2 }, Room, DeviceFilter.Computers));
        Assert.Equal(new[] { Cel2 }, DeviceSupport.ToolbarReach(action, new[] { Cel2 }, Room, DeviceFilter.Phones));
    }

    // A filter that shows nothing reaches nothing (the button then says so).
    [Theory]
    [InlineData(DeviceFilter.Phones)]
    [InlineData(DeviceFilter.Computers)]
    public void A_filter_with_none_of_its_kind_reaches_nothing(DeviceFilter shown)
    {
        var onlyTheOtherKind = shown == DeviceFilter.Phones ? new[] { Pc1, Pc2 } : new[] { Cel1, Cel2 };

        Assert.Empty(DeviceSupport.ToolbarReach(ToolbarAction.ScreenLock, None, onlyTheOtherKind, shown));
        Assert.Empty(DeviceSupport.ToolbarReach(ToolbarAction.Power, None, Array.Empty<KnownMachine>(), DeviceFilter.All));
    }

    // "Otimizar" and the PIN (Telas menu): the Android devices of the same reach.
    [Theory]
    [InlineData(ToolbarAction.Optimize)]
    [InlineData(ToolbarAction.PhonePin)]
    [InlineData(ToolbarAction.UpdatePhones)]
    public void Phone_actions_keep_the_android_devices_of_the_reach(ToolbarAction action)
    {
        Assert.Equal(new[] { Cel1, Cel2 }, DeviceSupport.ToolbarReach(action, None, Room, DeviceFilter.All));
        Assert.Equal(new[] { Cel1, Cel2 }, DeviceSupport.ToolbarReach(action, None, Room, DeviceFilter.Phones));
        Assert.Empty(DeviceSupport.ToolbarReach(action, None, Room, DeviceFilter.Computers));
        Assert.Equal(new[] { Cel2 }, DeviceSupport.ToolbarReach(action, new[] { Cel2 }, Room, DeviceFilter.All));
        Assert.Equal(new[] { Cel1 }, DeviceSupport.ToolbarReach(action, new[] { Pc1, Cel1 }, Room, DeviceFilter.All));
        Assert.Empty(DeviceSupport.ToolbarReach(action, new[] { Pc1 }, Room, DeviceFilter.All));
    }

    // The tiles' version goes by the same rule, through each tile's machine.
    [Fact]
    public void The_rule_over_tiles_is_the_same()
    {
        var tiles = Room.Select(machine => (Name: machine.Name, Machine: machine)).ToList();

        var reach = DeviceSupport.ToolbarReach(ToolbarAction.ScreenLock, Array.Empty<(string, KnownMachine)>(), tiles, DeviceFilter.Phones, tile => tile.Item2);

        Assert.Equal(new[] { "Cel 01", "Cel 02" }, reach.Select(tile => tile.Item1));
    }

    // With Celulares and nothing selected the reach is phones alone: what a
    // phone can't do is greyed, as for a selection of phones only.
    [Theory]
    [InlineData(ToolbarAction.ScreenLock, true)]
    [InlineData(ToolbarAction.BlockPrograms, true)]
    [InlineData(ToolbarAction.BlockSites, true)]
    [InlineData(ToolbarAction.CloseApps, true)]
    [InlineData(ToolbarAction.Message, true)]
    [InlineData(ToolbarAction.Chat, true)]
    [InlineData(ToolbarAction.Power, true)]
    [InlineData(ToolbarAction.Wake, false)]
    [InlineData(ToolbarAction.Schedule, false)]
    [InlineData(ToolbarAction.SendFile, true)]
    [InlineData(ToolbarAction.CollectFiles, true)]
    [InlineData(ToolbarAction.ClassFolder, true)]
    [InlineData(ToolbarAction.OpenWebsite, true)]
    [InlineData(ToolbarAction.OpenProgram, true)]
    [InlineData(ToolbarAction.Install, true)]
    [InlineData(ToolbarAction.Uninstall, true)]
    [InlineData(ToolbarAction.LabConfig, true)]
    public void Phones_filter_greys_what_a_phone_cannot_do(ToolbarAction action, bool enabled)
    {
        Assert.Equal(enabled, DeviceSupport.IsEnabled(DeviceSupport.ToolbarReach(action, None, Room, DeviceFilter.Phones), action));
        Assert.True(DeviceSupport.IsEnabled(DeviceSupport.ToolbarReach(action, None, Room, DeviceFilter.All), action));
        Assert.True(DeviceSupport.IsEnabled(DeviceSupport.ToolbarReach(action, None, Room, DeviceFilter.Computers), action));
    }

    [Fact]
    public void The_grid_and_the_reach_show_the_same_kinds()
    {
        Assert.True(DeviceSupport.Shows(Pc1, DeviceFilter.All));
        Assert.True(DeviceSupport.Shows(Cel1, DeviceFilter.All));
        Assert.True(DeviceSupport.Shows(Pc1, DeviceFilter.Computers));
        Assert.False(DeviceSupport.Shows(Cel1, DeviceFilter.Computers));
        Assert.False(DeviceSupport.Shows(Pc1, DeviceFilter.Phones));
        Assert.True(DeviceSupport.Shows(Cel1, DeviceFilter.Phones));
    }

    // The status bar, while nothing is selected and the filter leaves machines out.
    [Fact]
    public void The_reach_notice_says_whom_the_toolbar_reaches()
    {
        Assert.Equal("Só nos celulares (3).", DeviceSupport.ReachNotice(DeviceFilter.Phones, selected: 0, reached: 3, room: 11));
        Assert.Equal("Só nos computadores (8).", DeviceSupport.ReachNotice(DeviceFilter.Computers, selected: 0, reached: 8, room: 11));
        Assert.Null(DeviceSupport.ReachNotice(DeviceFilter.All, selected: 0, reached: 11, room: 11));
        Assert.Null(DeviceSupport.ReachNotice(DeviceFilter.Computers, selected: 0, reached: 8, room: 8));
        Assert.Null(DeviceSupport.ReachNotice(DeviceFilter.Phones, selected: 0, reached: 0, room: 8));
    }

    // A selection is the reach under every filter: a button that acted on it
    // says so, unless every machine of the room is selected.
    [Theory]
    [InlineData(DeviceFilter.All)]
    [InlineData(DeviceFilter.Computers)]
    [InlineData(DeviceFilter.Phones)]
    public void The_reach_notice_names_a_selection(DeviceFilter shown)
    {
        Assert.Equal("Só na seleção (1).", DeviceSupport.ReachNotice(shown, selected: 1, reached: 1, room: 20));
        Assert.Equal("Só na seleção (2).", DeviceSupport.ReachNotice(shown, selected: 2, reached: 2, room: 11));
        // Every machine of the room selected: the whole room, nothing to say.
        Assert.Null(DeviceSupport.ReachNotice(shown, selected: 11, reached: 11, room: 11));
    }

    [Fact]
    public void A_button_with_nothing_to_reach_says_which_kind_is_missing()
    {
        Assert.Equal("Nenhum celular na lista da sala para desligar.", DeviceSupport.NoneReachedText(DeviceFilter.Phones, "desligar"));
        Assert.Equal("Nenhum computador na lista da sala para bloquear sites.", DeviceSupport.NoneReachedText(DeviceFilter.Computers, "bloquear sites"));
        Assert.Equal("Nenhuma máquina na lista da sala para bloquear.", DeviceSupport.NoneReachedText(DeviceFilter.All, "bloquear"));
    }

    // "Bloquear telas" under each filter and selection: what one press locks
    // or gives back, run against a set of locks the way MainWindow keeps them.
    private sealed class Locks
    {
        public HashSet<KnownMachine> Locked { get; } = new();

        public bool Releases(IReadOnlyList<KnownMachine> reach, bool selection = false) =>
            ScreenLockReach.Releases(reach, machine => machine, Locked.Contains, selection);

        public void Press(IReadOnlyList<KnownMachine> reach, bool selection = false)
        {
            var (toLock, toUnlock) = ScreenLockReach.Plan(reach, machine => machine, Locked.Contains, selection);
            Locked.UnionWith(toLock);
            Locked.ExceptWith(toUnlock);
        }

        public void Press(DeviceFilter shown, params KnownMachine[] selected) =>
            Press(DeviceSupport.ToolbarReach(ToolbarAction.ScreenLock, selected, Room, shown), selection: selected.Length > 0);
    }

    [Fact]
    public void Lock_with_celulares_locks_only_the_phones()
    {
        var locks = new Locks();

        locks.Press(DeviceFilter.Phones);

        Assert.Equal(new[] { Cel1, Cel2 }, Room.Where(locks.Locked.Contains));
    }

    [Fact]
    public void Lock_with_computadores_locks_only_the_pcs()
    {
        var locks = new Locks();

        locks.Press(DeviceFilter.Computers);

        Assert.Equal(new[] { Pc1, Pc2 }, Room.Where(locks.Locked.Contains));
    }

    [Fact]
    public void Lock_with_todos_locks_the_whole_room_and_then_gives_it_back()
    {
        var locks = new Locks();

        locks.Press(DeviceFilter.All);
        Assert.Equal(Room, Room.Where(locks.Locked.Contains));
        Assert.True(locks.Releases(Room));

        locks.Press(DeviceFilter.All);
        Assert.Empty(locks.Locked);
    }

    [Fact]
    public void Lock_with_a_selection_locks_only_the_selection_under_every_filter()
    {
        var locks = new Locks();

        locks.Press(DeviceFilter.All, Pc2);
        Assert.Equal(new[] { Pc2 }, locks.Locked);

        locks.Press(DeviceFilter.Phones, Cel1);
        Assert.Equal(new[] { Cel1, Pc2 }, Room.Where(locks.Locked.Contains));
    }

    // The PCs locked before stay locked when the teacher switches to Celulares:
    // the button then reaches the phones only, and gives back only them.
    [Fact]
    public void Switching_the_filter_keeps_the_locks_made_before()
    {
        var locks = new Locks();
        locks.Press(DeviceFilter.All);

        // Celulares: every phone locked, so the button gives them back...
        var phones = DeviceSupport.ToolbarReach(ToolbarAction.ScreenLock, None, Room, DeviceFilter.Phones);
        Assert.True(locks.Releases(phones));
        locks.Press(DeviceFilter.Phones);

        // ... and the PCs, out of its reach, keep their lock.
        Assert.Equal(new[] { Pc1, Pc2 }, Room.Where(locks.Locked.Contains));

        // Back to Todos with nothing selected: some are still locked, so the
        // button gives those back, as it always did.
        Assert.True(locks.Releases(Room));
        locks.Press(DeviceFilter.All);
        Assert.Empty(locks.Locked);
    }

    // Phones locked with Celulares, then Computadores: the button locks the PCs
    // (none of them is locked) and the phones stay locked.
    [Fact]
    public void Locking_the_other_kind_leaves_the_first_one_locked()
    {
        var locks = new Locks();
        locks.Press(DeviceFilter.Phones);

        Assert.False(locks.Releases(DeviceSupport.ToolbarReach(ToolbarAction.ScreenLock, None, Room, DeviceFilter.Computers)));
        locks.Press(DeviceFilter.Computers);

        Assert.Equal(Room, Room.Where(locks.Locked.Contains));
    }

    // A selection partly locked: the press locks the rest of it, and gives
    // nothing back.
    [Fact]
    public void A_partly_locked_selection_is_locked_the_rest_of_the_way()
    {
        var locks = new Locks();
        locks.Press(DeviceFilter.All, Cel1);

        var selection = new[] { Pc1, Cel1, Pc2 };
        Assert.False(locks.Releases(selection, selection: true));
        var (toLock, toUnlock) = ScreenLockReach.Plan(selection, machine => machine, locks.Locked.Contains, selection: true);

        Assert.Equal(new[] { Pc1, Pc2 }, toLock);
        Assert.Empty(toUnlock);
    }

    // With nothing selected, as before the selection rule: one student
    // locked from the tile's menu is given back by the toolbar's press, and
    // the other 19 are not locked.
    [Fact]
    public void One_of_20_locked_with_nothing_selected_is_given_back()
    {
        var room = Enumerable.Range(1, 20)
            .Select(i => new KnownMachine($"Aluno-{i:00}", $"192.0.2.{i}", "ANY", null))
            .ToArray();
        var locked = new HashSet<KnownMachine> { room[2] };
        var reach = DeviceSupport.ToolbarReach(ToolbarAction.ScreenLock, None, room, DeviceFilter.All);

        Assert.True(ScreenLockReach.Releases(reach, machine => machine, locked.Contains, selection: false));
        var (toLock, toUnlock) = ScreenLockReach.Plan(reach, machine => machine, locked.Contains, selection: false);

        Assert.Empty(toLock);
        Assert.Equal(new[] { room[2] }, toUnlock);
    }

    // A machine that joins a locked room: the first press still gives the
    // room back, it does not lock the newcomer first.
    [Fact]
    public void A_machine_joining_a_locked_room_does_not_hold_back_the_release()
    {
        var locks = new Locks();
        locks.Locked.UnionWith(new[] { Pc1, Cel1, Pc2 });

        Assert.True(locks.Releases(Room));
        locks.Press(DeviceFilter.All);

        Assert.Empty(locks.Locked);
    }

    // With a selection the rule stays: one locked among the selected ones
    // locks the rest of them first.
    [Fact]
    public void One_locked_in_a_selection_locks_the_rest_of_it()
    {
        var locks = new Locks();
        locks.Locked.Add(Pc1);

        locks.Press(DeviceFilter.All, Pc1, Pc2);
        Assert.Equal(new[] { Pc1, Pc2 }, Room.Where(locks.Locked.Contains));

        locks.Press(DeviceFilter.All, Pc1, Pc2);
        Assert.Empty(locks.Locked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Nothing_to_reach_neither_locks_nor_gives_back(bool selection)
    {
        Assert.False(ScreenLockReach.Releases(None, machine => machine, _ => true, selection));
        var (toLock, toUnlock) = ScreenLockReach.Plan(None, machine => machine, _ => true, selection);

        Assert.Empty(toLock);
        Assert.Empty(toUnlock);
    }
}
