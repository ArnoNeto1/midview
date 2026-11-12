using Midview.Master;
using Midview.Protocol;
using Xunit;

public class DeviceSupportTests
{
    private static KnownMachine Pc(string name) => new(name, "192.0.2.1", "ANY", null);

    private static KnownMachine Phone(string name) => new(name, "192.0.2.9", "ANY", null, Kind: DeviceKind.Android);

    [Fact]
    public void Phone_only_selection_disables_pc_actions() =>
        Assert.False(DeviceSupport.IsEnabled(new[] { Phone("Cel 01") }, ToolbarAction.Power));

    [Fact]
    public void A_computer_in_the_selection_keeps_the_action_enabled() =>
        Assert.True(DeviceSupport.IsEnabled(new[] { Phone("Cel 01"), Pc("PC 01") }, ToolbarAction.Power));

    [Fact]
    public void Nothing_selected_enables_nothing() =>
        Assert.False(DeviceSupport.IsEnabled(Array.Empty<KnownMachine>(), ToolbarAction.View));

    [Theory]
    [InlineData(ToolbarAction.View)]
    [InlineData(ToolbarAction.Control)]
    [InlineData(ToolbarAction.ScreenLock)]
    [InlineData(ToolbarAction.Power)]
    [InlineData(ToolbarAction.BlockSites)]
    [InlineData(ToolbarAction.Update)]
    public void Computers_support_every_action(ToolbarAction action) =>
        Assert.True(DeviceSupport.Supports(DeviceKind.Windows, action));

    [Fact]
    public void Phone_supports_view_screen_lock_blocking_and_its_own_actions_only()
    {
        foreach (var action in Enum.GetValues<ToolbarAction>())
        {
            var expected = action is ToolbarAction.View or ToolbarAction.ScreenLock
                or ToolbarAction.BlockPrograms or ToolbarAction.BlockSites
                or ToolbarAction.Optimize or ToolbarAction.UpdatePhones;
            Assert.Equal(expected, DeviceSupport.Supports(DeviceKind.Android, action));
        }
    }

    // "Otimizar" and "Atualizar celulares" are made for phones: a PC has nothing to do with them.
    [Theory]
    [InlineData(ToolbarAction.Optimize)]
    [InlineData(ToolbarAction.UpdatePhones)]
    public void Phone_actions_are_for_phones_only(ToolbarAction action)
    {
        Assert.False(DeviceSupport.Supports(DeviceKind.Windows, action));
        Assert.False(DeviceSupport.IsEnabled(new[] { Pc("PC 01") }, action));
        Assert.True(DeviceSupport.IsEnabled(new[] { Pc("PC 01"), Phone("Cel 01") }, action));
    }

    // The CELULARES group: the selected phones, or every phone of the room
    // when no phone is selected (a selection of PCs alone included).
    [Theory]
    [InlineData(ToolbarAction.Optimize)]
    [InlineData(ToolbarAction.UpdatePhones)]
    public void Phone_actions_reach_the_selected_phones_or_every_phone(ToolbarAction action)
    {
        var pc = Pc("PC 01");
        var cel1 = Phone("Cel 01");
        var cel2 = Phone("Cel 02");
        var room = new[] { pc, cel1, cel2 };

        Assert.Equal(new[] { cel1, cel2 }, DeviceSupport.ToolbarReach(action, Array.Empty<KnownMachine>(), room, DeviceFilter.All));
        Assert.Equal(new[] { cel2 }, DeviceSupport.ToolbarReach(action, new[] { cel2 }, room, DeviceFilter.All));
        Assert.Equal(new[] { cel1 }, DeviceSupport.ToolbarReach(action, new[] { pc, cel1 }, room, DeviceFilter.All));
        Assert.Equal(new[] { cel1, cel2 }, DeviceSupport.ToolbarReach(action, new[] { pc }, room, DeviceFilter.All));
        Assert.Equal(new[] { cel1, cel2 }, DeviceSupport.ToolbarReach(action, Array.Empty<KnownMachine>(), room, DeviceFilter.Computers));
        Assert.Empty(DeviceSupport.ToolbarReach(action, Array.Empty<KnownMachine>(), new[] { pc }, DeviceFilter.All));
    }

    // The CELULARES group shows only in a room with a phone: a room of PCs stays as it was.
    [Fact]
    public void Phone_group_shows_only_with_a_phone_in_the_room()
    {
        Assert.False(DeviceSupport.ShowsPhoneGroup(Array.Empty<KnownMachine>()));
        Assert.False(DeviceSupport.ShowsPhoneGroup(new[] { Pc("PC 01"), Pc("PC 02") }));
        Assert.True(DeviceSupport.ShowsPhoneGroup(new[] { Pc("PC 01"), Phone("Cel 01") }));
        Assert.True(DeviceSupport.ShowsPhoneGroup(new[] { Phone("Cel 01") }));
    }

    // The sites window sends its commands to every machine it reaches.
    [Theory]
    [InlineData(ToolbarAction.BlockPrograms)]
    [InlineData(ToolbarAction.BlockSites)]
    public void Blocking_leaves_no_phone_out(ToolbarAction action)
    {
        var machines = new[] { Pc("PC 01"), Phone("Cel 01") };

        Assert.Null(DeviceSupport.LeftOutNotice(machines, action));
        Assert.True(DeviceSupport.IsEnabled(new[] { Phone("Cel 01") }, action));
        Assert.Equal(new[] { "PC 01", "Cel 01" }, DeviceSupport.Only(machines, action).Select(machine => machine.Name));
    }

    [Fact]
    public void Phone_only_selection_keeps_control_grey_but_view_and_lock_on()
    {
        var selected = new[] { Phone("Cel 01") };

        Assert.True(DeviceSupport.IsEnabled(selected, ToolbarAction.View));
        Assert.True(DeviceSupport.IsEnabled(selected, ToolbarAction.ScreenLock));
        Assert.False(DeviceSupport.IsEnabled(selected, ToolbarAction.Control));
    }

    [Fact]
    public void Lock_of_a_mixed_class_leaves_no_phone_out() =>
        Assert.Null(DeviceSupport.LeftOutNotice(new[] { Pc("PC 01"), Phone("Cel 01") }, ToolbarAction.ScreenLock));

    [Fact]
    public void Control_of_a_phone_is_left_out() =>
        Assert.Equal("1 celular ficou de fora: essa ação ainda não funciona em celular.",
                     DeviceSupport.LeftOutNotice(new[] { Phone("Cel 01") }, ToolbarAction.Control));

    // The Ctrl+Alt+Del escapes are a Windows Agent thing (port 28900): a phone has no Agent for it.
    [Fact]
    public void Phone_lock_does_not_start_switch_user_block()
    {
        Assert.True(SwitchUserBlockKeeper.AppliesTo(new KnownMachine("PC 01", "192.0.2.1", "ANY", "AGENT")));
        Assert.False(SwitchUserBlockKeeper.AppliesTo(new KnownMachine("PC 02", "192.0.2.2", "ANY", null)));
        Assert.False(SwitchUserBlockKeeper.AppliesTo(
            new KnownMachine("Cel 01", "192.0.2.9", "ANY", "AGENT", Kind: DeviceKind.Android)));
    }

    [Fact]
    public void Mixed_selection_warns_about_phones_left_out() =>
        Assert.Equal("2 celulares ficaram de fora: essa ação ainda não funciona em celular.",
                     DeviceSupport.LeftOutNotice(new[] { Pc("PC 01"), Phone("Cel 01"), Phone("Cel 02") }, ToolbarAction.Power));

    [Fact]
    public void One_phone_left_out_uses_singular() =>
        Assert.Equal("1 celular ficou de fora: essa ação ainda não funciona em celular.",
                     DeviceSupport.LeftOutNotice(new[] { Pc("PC 01"), Phone("Cel 01") }, ToolbarAction.Power));

    [Fact]
    public void Nobody_left_out_is_no_notice() =>
        Assert.Null(DeviceSupport.LeftOutNotice(new[] { Pc("PC 01"), Pc("PC 02") }, ToolbarAction.Power));

    // What PublishRoster feeds the class folder, and the update window its targets.
    [Theory]
    [InlineData(ToolbarAction.ClassFolder)]
    [InlineData(ToolbarAction.Update)]
    public void Only_leaves_phones_out_of_the_class_folder_and_the_update(ToolbarAction action)
    {
        var machines = new[] { Pc("PC 01"), Phone("Cel 01"), Pc("PC 02") };

        Assert.Equal(new[] { "PC 01", "PC 02" }, DeviceSupport.Only(machines, action).Select(machine => machine.Name));
    }

    // The toolbar's "Bloquear programas" and "Bloquear sites" reach the whole
    // room, as every toolbar button does, except a selection of phones only,
    // or the "Celulares" filter with nothing selected: then those phones; and
    // the "Computadores" filter: then the PCs.
    [Theory]
    [InlineData(ToolbarAction.BlockPrograms)]
    [InlineData(ToolbarAction.BlockSites)]
    public void Toolbar_blocking_reaches_the_room_unless_only_phones_are_chosen(ToolbarAction action)
    {
        var pc = Pc("PC 01");
        var cel1 = Phone("Cel 01");
        var cel2 = Phone("Cel 02");
        var room = new[] { pc, cel1, cel2 };

        Assert.Equal(room, DeviceSupport.ToolbarReach(action, Array.Empty<KnownMachine>(), room, DeviceFilter.All));
        Assert.Equal(room, DeviceSupport.ToolbarReach(action, new[] { pc }, room, DeviceFilter.All));
        Assert.Equal(room, DeviceSupport.ToolbarReach(action, new[] { pc, cel1 }, room, DeviceFilter.All));
        Assert.Equal(new[] { pc }, DeviceSupport.ToolbarReach(action, Array.Empty<KnownMachine>(), room, DeviceFilter.Computers));
        Assert.Equal(new[] { cel2 }, DeviceSupport.ToolbarReach(action, new[] { cel2 }, room, DeviceFilter.All));
        Assert.Equal(new[] { cel1, cel2 }, DeviceSupport.ToolbarReach(action, Array.Empty<KnownMachine>(), room, DeviceFilter.Phones));
        Assert.Equal(new[] { cel1 }, DeviceSupport.ToolbarReach(action, new[] { cel1 }, room, DeviceFilter.Phones));
    }

    // The "Computadores" filter shows the PCs only: blocking from the toolbar
    // reaches every PC, as before phones could be blocked, and never a phone
    // the teacher cannot see there. A selection of phones only still reaches
    // them; a room with no PC, nothing.
    [Theory]
    [InlineData(ToolbarAction.BlockPrograms)]
    [InlineData(ToolbarAction.BlockSites)]
    public void Computers_filter_keeps_the_hidden_phones_out(ToolbarAction action)
    {
        var pc1 = Pc("PC 01");
        var cel = Phone("Cel 01");
        var pc2 = Pc("PC 02");
        var room = new[] { pc1, cel, pc2 };

        Assert.Equal(new[] { pc1, pc2 }, DeviceSupport.ToolbarReach(action, Array.Empty<KnownMachine>(), room, DeviceFilter.Computers));
        Assert.Equal(new[] { pc1, pc2 }, DeviceSupport.ToolbarReach(action, new[] { pc1 }, room, DeviceFilter.Computers));
        Assert.Equal(new[] { pc1, pc2 }, DeviceSupport.ToolbarReach(action, new[] { pc1, cel }, room, DeviceFilter.Computers));
        Assert.Equal(new[] { cel }, DeviceSupport.ToolbarReach(action, new[] { cel }, room, DeviceFilter.Computers));
        Assert.Empty(DeviceSupport.ToolbarReach(action, Array.Empty<KnownMachine>(), new[] { cel }, DeviceFilter.Computers));
    }

    // Every other toolbar button (locking the screens among them) keeps
    // reaching the whole room.
    [Theory]
    [InlineData(ToolbarAction.ScreenLock)]
    [InlineData(ToolbarAction.Power)]
    [InlineData(ToolbarAction.CloseApps)]
    public void Other_toolbar_buttons_reach_the_whole_room(ToolbarAction action)
    {
        var room = new[] { Pc("PC 01"), Phone("Cel 01") };

        Assert.Equal(room, DeviceSupport.ToolbarReach(action, new[] { room[1] }, room, DeviceFilter.All));
        Assert.Equal(room, DeviceSupport.ToolbarReach(action, Array.Empty<KnownMachine>(), room, DeviceFilter.Phones));
    }

    [Fact]
    public void Action_names_in_the_xaml_parse_to_actions()
    {
        var item = new System.Windows.DependencyObject();
        Assert.Null(DeviceActions.Of(item));
        DeviceActions.SetAction(item, "SendFile");
        Assert.Equal(ToolbarAction.SendFile, DeviceActions.Of(item));
    }
}
